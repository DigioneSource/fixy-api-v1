using Application.Common;
using Application.Common.Interfaces;
using Application.DTOs.Address;
using Application.DTOs.Media;
using Application.DTOs.WorkerProfile;
using Application.DTOs.WorkerProfile.WorkerCertificate;
using Application.DTOs.WorkerProfile.WorkerService;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Application.Interfaces.Services.Media;
using Domain.Entity;
using Domain.Enum;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services
{
    public class WorkerProfileService : IWorkerProfileService
    {
        private readonly IUserRepository _userRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IAddressRepository _addressRepository;
        private readonly IWorkerProfileRepository _workerProfileRepository;
        private readonly IWorkerServiceRepository _workerServiceRepository;
        private readonly IWorkerCertificateRepository _workerCertificateRepository;
        private readonly IWalletRepository _walletRepository;

        private readonly IMediaRepository _mediaRepository;
        private readonly IUnitOfWork _unitOfWork;

        private readonly IBlobService _blobService;
        private readonly IWorkerWeeklyScheduleService _workerWeeklyScheduleService;
        private readonly IGoongService _goongService;
        private readonly IBookingRepository _bookingRepository;
        private readonly IPayoutRequestRepository _payoutRequestRepository;
        private readonly IWorkerPayoutAccountRepository _workerPayoutAccountRepository;
        private readonly IWalletTransactionRepository _walletTransactionRepository;
        private readonly IFaceRecognitionService _faceRecognitionService;
        private readonly ILogger<WorkerProfileService> _logger;

        public WorkerProfileService(
            IUserRepository userRepository,
            IAddressRepository addressRepository,
            IWorkerProfileRepository workerProfileRepository,
            IWorkerServiceRepository workerServiceRepository,
            IWorkerCertificateRepository workerCertificateRepository,
            IWalletRepository walletRepository,
            IMediaRepository mediaRepository,
            IUnitOfWork unitOfWork,
            IBlobService blobService,
            IWorkerWeeklyScheduleService workerWeeklyScheduleService,
            ICurrentUserService currentUserService,
            IGoongService goongService,
            IBookingRepository bookingRepository,
            IPayoutRequestRepository payoutRequestRepository,
            IWorkerPayoutAccountRepository workerPayoutAccountRepository,
            IWalletTransactionRepository walletTransactionRepository,
            IFaceRecognitionService faceRecognitionService,
            ILogger<WorkerProfileService> logger
        )
        {
            _userRepository = userRepository;
            _addressRepository = addressRepository;
            _workerProfileRepository = workerProfileRepository;
            _workerServiceRepository = workerServiceRepository;
            _workerCertificateRepository = workerCertificateRepository;
            _walletRepository = walletRepository;
            _mediaRepository = mediaRepository;
            _unitOfWork = unitOfWork;
            _blobService = blobService;
            _workerWeeklyScheduleService = workerWeeklyScheduleService;
            _currentUserService = currentUserService;
            _goongService = goongService;
            _bookingRepository = bookingRepository;
            _payoutRequestRepository = payoutRequestRepository;
            _workerPayoutAccountRepository = workerPayoutAccountRepository;
            _walletTransactionRepository = walletTransactionRepository;
            _faceRecognitionService = faceRecognitionService;
            _logger = logger;
        }

        public async Task<OperationResult<PagedResponse<WorkerProfileDto>>> GetPagedWorkerProfiles(
            WorkerProfileQuery query,
            string? role,
            CancellationToken cancellationToken
        )
        {
            if (role != "ADMIN")
            {
                query.Status = WorkerStatus.Approved;
            }

            var (items, totalCount) = await _workerProfileRepository.GetWorkerProfilesAsync(
                query,
                cancellationToken
            );

            var dtoItems = items
                .Select(i => new WorkerProfileDto
                {
                    Id = i.Id,
                    UserId = i.UserId,
                    FullName = i.User!.FullName,
                    DateOfBirth = i.User.DateOfBirth,
                    Gender = i.User.Gender.ToString(),
                    Status = i.Status.ToString(),
                    IsOffboardingRequested = i.IsOffboardingRequested,
                    ExperienceYears = i.ExperienceYears,
                    RatingAvg = i.RatingAvg,
                    TotalReviews = i.TotalReviews,
                    TotalOrders = i.TotalOrders,
                    City = i.Address?.City,

                    Services = i.Services.Select(MapWorkerService).ToList(),
                })
                .ToList();

            return OperationResult<PagedResponse<WorkerProfileDto>>.Success(
                new PagedResponse<WorkerProfileDto>
                {
                    Items = dtoItems,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                    TotalCount = totalCount,
                },
                "Get worker profiles successfully"
            );
        }

        public async Task<OperationResult<WorkerPublicDetailDto>> GetPublicDetailAsync(
            Guid workerId,
            CancellationToken cancellationToken
        )
        {
            var data = await GetWorkerProfileDataAsync(workerId, cancellationToken);

            return OperationResult<WorkerPublicDetailDto>.Success(
                new WorkerPublicDetailDto
                {
                    Id = data.WorkerProfile.Id,
                    UserId = data.WorkerProfile.UserId,

                    FullName = data.WorkerProfile.User!.FullName,

                    Bio = data.WorkerProfile.Bio,

                    ExperienceYears = data.WorkerProfile.ExperienceYears,

                    RatingAvg = data.WorkerProfile.RatingAvg,

                    TotalReviews = data.WorkerProfile.TotalReviews,

                    TotalOrders = data.WorkerProfile.TotalOrders,

                    Services = data.WorkerProfile.Services.Select(MapWorkerService).ToList(),

                    Certificates = data
                        .WorkerProfile.Certificates.Select(x =>
                            MapCertificate(x, data.CertificateImageLookup)
                        )
                        .ToList(),

                    PortfolioImages = data.PortfolioImages.Select(MapMedia).ToList(),
                },
                "Get worker public detail successfully"
            );
        }

        public async Task<OperationResult<WorkerPrivateDetailDto>> GetPrivateDetailAsync(
            Guid workerId,
            CancellationToken cancellationToken
        )
        {
            var data = await GetWorkerProfileDataAsync(workerId, cancellationToken);

            return OperationResult<WorkerPrivateDetailDto>.Success(
                new WorkerPrivateDetailDto
                {
                    Id = data.WorkerProfile.Id,
                    UserId = data.WorkerProfile.UserId,

                    FullName = data.WorkerProfile.User!.FullName,

                    Email = data.WorkerProfile.User.Email!,

                    Phone = data.WorkerProfile.User.Phone!,

                    Bio = data.WorkerProfile.Bio,

                    ExperienceYears = data.WorkerProfile.ExperienceYears,

                    RatingAvg = data.WorkerProfile.RatingAvg,

                    TotalReviews = data.WorkerProfile.TotalReviews,

                    TotalOrders = data.WorkerProfile.TotalOrders,

                    Services = data.WorkerProfile.Services.Select(MapWorkerService).ToList(),

                    Certificates = data
                        .WorkerProfile.Certificates.Select(x =>
                            MapCertificate(x, data.CertificateImageLookup)
                        )
                        .ToList(),

                    PortfolioImages = data.PortfolioImages.Select(MapMedia).ToList(),
                },
                "Get worker private detail successfully"
            );
        }

        public async Task<
            OperationResult<WorkerAdminAndOwnerDetailDto>
        > GetAdminAndOwnerDetailAsync(Guid workerId, CancellationToken cancellationToken)
        {
            var data = await GetWorkerProfileDataAsync(workerId, cancellationToken);

            var address = data.WorkerProfile.Address;

            return OperationResult<WorkerAdminAndOwnerDetailDto>.Success(
                new WorkerAdminAndOwnerDetailDto
                {
                    Id = data.WorkerProfile.Id,
                    UserId = data.WorkerProfile.UserId,

                    FullName = data.WorkerProfile.User!.FullName,

                    AvatarUrl = data.WorkerProfile.User?.AvatarUrl,

                    Email = data.WorkerProfile.User.Email!,

                    Phone = data.WorkerProfile.User.Phone!,

                    Gender = data.WorkerProfile.User.Gender.ToString(),

                    DateOfBirth = data.WorkerProfile.User.DateOfBirth,

                    Status = data.WorkerProfile.Status,

                    IsOnline = data.WorkerProfile.IsOnline,
                    IsAcceptingJobs = data.WorkerProfile.IsAcceptingJobs,
                    IsBusy = data.WorkerProfile.IsBusy,
                    IsOffboardingRequested = data.WorkerProfile.IsOffboardingRequested,
                    OffboardingRequestedAt = data.WorkerProfile.OffboardingRequestedAt,

                    Bio = data.WorkerProfile.Bio,

                    ExperienceYears = data.WorkerProfile.ExperienceYears,

                    MaxDistanceKm = data.WorkerProfile.MaxDistanceKm,

                    RatingAvg = data.WorkerProfile.RatingAvg,

                    TotalReviews = data.WorkerProfile.TotalReviews,

                    TotalOrders = data.WorkerProfile.TotalOrders,

                    CitizenIdNumber = data.WorkerProfile.User.CitizenIdNumber,

                    CitizenIdIssueDate = data.WorkerProfile.User.CitizenIdIssueDate,

                    CitizenIdIssuePlace = data.WorkerProfile.User.CitizenIdIssuePlace,

                    RejectReason = data.WorkerProfile.RejectReason,

                    Address =
                        address == null
                            ? null
                            : new AddressDto
                            {
                                Id = address.Id,
                                City = address.City,
                                Ward = address.Ward,
                                Detail = address.Detail,
                                Lat = address.Lat,
                                Lng = address.Lng,
                            },

                    Services = data.WorkerProfile.Services.Select(MapWorkerService).ToList(),

                    Certificates = data
                        .WorkerProfile.Certificates.Select(x =>
                            MapCertificate(x, data.CertificateImageLookup)
                        )
                        .ToList(),

                    PortfolioImages = data.PortfolioImages.Select(MapMedia).ToList(),

                    IdentificationImages = data.IdentificationImages.Select(MapMedia).ToList(),
                },
                "Get worker detail successfully"
            );
        }

        public async Task<OperationResult> WorkerRegisterAsync(
            WorkerRegisterRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            if (dto.WorkerService.Count is < 1 or > 10)
            {
                return OperationResult.Failure(
                    "Kĩ thuật viên chỉ được chọn từ 1 đến tối đa 10 dịch vụ."
                );
            }
            if (dto.CreateAddressRequestDto == null)
            {
                return OperationResult.Failure("Vui lòng cung cấp đầy đủ thông tin địa chỉ hoạt động.");
            }
            if (dto.PortfolioUploads.Count > 10)
            {
                return OperationResult.Failure(
                    "Chỉ được tải lên tối đa 10 hình ảnh hoạt động (Portfolio)."
                );
            }

            if (dto.IdentificationUploads.Count != 2)
            {
                return OperationResult.Failure("Vui lòng tải đủ 2 mặt (Mặt trước và Mặt sau) của CCCD.");
            }

            if (dto.WorkerService.Count(x => x.IsPrimary) != 1)
            {
                return OperationResult.Failure("Vui lòng chọn đúng 1 dịch vụ chính.");
            }

            if (string.IsNullOrWhiteSpace(dto.CitizenIdNumber))
            {
                return OperationResult.Failure("Vui lòng cung cấp số CCCD định danh.");
            }

            User? user = null;
            if (_currentUserService.UserId != null && Guid.TryParse(_currentUserService.UserId, out var currentUserId))
            {
                user = await _userRepository.GetByIdAsync(currentUserId, cancellationToken);
            }

            if (user == null && !string.IsNullOrWhiteSpace(dto.Target))
            {
                user = await _userRepository.GetByTargetAsync(dto.Target, cancellationToken);
            }

            if (user == null)
            {
                return OperationResult.Failure("User not found");
            }

            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                user.Phone = dto.Phone.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.Target))
            {
                var targetStr = dto.Target.Trim();
                if (targetStr.Contains('@'))
                {
                    if (string.IsNullOrWhiteSpace(user.Email) || user.Email != targetStr)
                    {
                        user.Email = targetStr;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(user.Phone))
                    {
                        user.Phone = targetStr;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(user.Phone))
            {
                var isPhoneDuplicate = await _userRepository.ExistsAsync(
                    u => u.Phone == user.Phone && u.Id != user.Id,
                    cancellationToken
                );
                if (isPhoneDuplicate)
                {
                    return OperationResult.Failure("Số điện thoại này đã được sử dụng bởi một tài khoản khác.");
                }
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                var isEmailDuplicate = await _userRepository.ExistsAsync(
                    u => u.Email == user.Email && u.Id != user.Id,
                    cancellationToken
                );
                if (isEmailDuplicate)
                {
                    return OperationResult.Failure("Email này đã được sử dụng bởi một tài khoản khác.");
                }
            }

            _userRepository.Update(user);
            var hasExistingWorker = await _workerProfileRepository.ExistsAsync(
                x => x.UserId == user.Id,
                cancellationToken
            );

            if (hasExistingWorker)
            {
                return OperationResult.Failure("Tài khoản này đã đăng ký hồ sơ kỹ thuật viên trước đó.");
            }

            var isCitizenIdDuplicate = await _userRepository.ExistsAsync(
                u => u.CitizenIdNumber == dto.CitizenIdNumber && u.Id != user.Id,
                cancellationToken
            );
            if (isCitizenIdDuplicate)
            {
                return OperationResult.Failure("Số CCCD này đã được đăng ký bởi một tài khoản khác.");
            }

            user.CitizenIdNumber = dto.CitizenIdNumber;
            user.CitizenIdIssueDate = dto.CitizenIdIssueDate;
            user.CitizenIdIssuePlace = dto.CitizenIdIssuePlace;
            user.IsCitizenIdVerified = true;
            var uploadedUrls = new List<string>();

            using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                // Create Worker Profile
                var workerProfile = new WorkerProfile
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Bio = dto.Bio,
                    ExperienceYears = dto.ExperienceYears,
                    MaxDistanceKm = dto.MaxDistanceKm,
                    Status = WorkerStatus.Pending,
                    Badge = WorkerBadge.NewArrival,
                    RatingAvg = 0,
                    TotalReviews = 0,
                    TotalOrders = 0,
                    IsOnline = false,
                };

                await _workerProfileRepository.AddAsync(workerProfile, cancellationToken);
                // Create Worker Schedule
                await _workerWeeklyScheduleService.CreateDefaultScheduleAsync(
                    workerProfile.Id,
                    false,
                    cancellationToken
                );
                // Create Worker Address
                var workerAddress = new Address
                {
                    WorkerProfileId = workerProfile.Id,
                    City = dto.CreateAddressRequestDto.City,
                    Ward = dto.CreateAddressRequestDto.Ward,
                    Detail = dto.CreateAddressRequestDto.Detail,
                    Lat = dto.CreateAddressRequestDto.Lat,
                    Lng = dto.CreateAddressRequestDto.Lng,
                    IsDefault = true,
                };

                if ((!workerAddress.Lat.HasValue || workerAddress.Lat == 0) && _goongService != null)
                {
                    var fullAddr = $"{workerAddress.Detail}, {workerAddress.Ward}, {workerAddress.City}";
                    var (lat, lng) = await _goongService.GeocodeAddressAsync(fullAddr);
                    if (lat.HasValue && lng.HasValue)
                    {
                        workerAddress.Lat = lat;
                        workerAddress.Lng = lng;
                    }
                }

                await _addressRepository.AddAsync(workerAddress, cancellationToken);
                // Create Worker Services

                foreach (var service in dto.WorkerService)
                {
                    var workerService = new WorkerService
                    {
                        WorkerProfileId = workerProfile.Id,
                        CategoryId = service.CategoryId,
                        BasePrice = service.BasePrice,
                        IsPrimary = service.IsPrimary,
                    };

                    if (service.Options != null && service.Options.Any())
                    {
                        var sortOrder = 1;
                        foreach (var opt in service.Options)
                        {
                            workerService.Options.Add(new WorkerServiceOption
                            {
                                DurationMinutes = opt.DurationMinutes,
                                Price = opt.Price,
                                SortOrder = opt.SortOrder ?? sortOrder++,
                                IsActive = opt.IsActive ?? true
                            });
                        }

                        if (workerService.BasePrice <= 0)
                        {
                            workerService.BasePrice = workerService.Options.Min(x => x.Price);
                        }
                    }

                    await _workerServiceRepository.AddAsync(workerService, cancellationToken);
                }
                // Upload Portfolio Images

                foreach (var upload in dto.PortfolioUploads)
                {
                    var imageUrl = await _blobService.UploadImageAsync(upload);

                    uploadedUrls.Add(imageUrl);

                    var media = new Media
                    {
                        OwnerId = user.Id,
                        UploadedById = user.Id,
                        OwnerType = MediaOwnerType.WorkerProfile,
                        Category = MediaCategory.Portfolio,
                        FileUrl = imageUrl,
                    };

                    await _mediaRepository.AddAsync(media, cancellationToken);
                }
                // Upload Identification Images

                foreach (var upload in dto.IdentificationUploads)
                {
                    var imageUrl = await _blobService.UploadImageAsync(upload);

                    uploadedUrls.Add(imageUrl);

                    var media = new Media
                    {
                        OwnerId = user.Id,
                        UploadedById = user.Id,
                        OwnerType = MediaOwnerType.User,
                        Category = MediaCategory.Identification,
                        FileUrl = imageUrl,
                    };

                    await _mediaRepository.AddAsync(media, cancellationToken);
                }

                if (dto.FaceSelfieUpload != null)
                {
                    var faceUrl = await _blobService.UploadImageAsync(dto.FaceSelfieUpload);
                    uploadedUrls.Add(faceUrl);

                    // Điểm khớp do máy chủ tự tính, không nhận từ client
                    var faceResult = await ComputeFaceMatchAsync(
                        dto.IdentificationUploads.FirstOrDefault(),
                        dto.FaceSelfieUpload,
                        user.Id,
                        cancellationToken
                    );

                    user.FaceImageUrl = faceUrl;
                    user.IsFaceMatched = faceResult.IsMatch;
                    user.FaceMatchScore = faceResult.Score;
                    user.FaceVerifiedAt = faceResult.Score.HasValue ? DateTime.UtcNow : null;
                }

                // Create Certificates

                foreach (var certificate in dto.CertificateUploads)
                {
                    var workerCertificate = new WorkerCertificate
                    {
                        WorkerProfileId = workerProfile.Id,
                        Title = certificate.Title,
                        IssuedBy = certificate.IssuedBy,
                        IssuedAt = certificate.IssuedAt.HasValue
                            ? DateOnly.FromDateTime(certificate.IssuedAt.Value)
                            : null,
                    };

                    await _workerCertificateRepository.AddAsync(
                        workerCertificate,
                        cancellationToken
                    );

                    // Upload Certificate Images

                    foreach (var upload in certificate.MediaUploads)
                    {
                        var imageUrl = await _blobService.UploadImageAsync(upload);

                        uploadedUrls.Add(imageUrl);

                        var media = new Media
                        {
                            OwnerId = workerCertificate.Id,
                            UploadedById = user.Id,
                            OwnerType = MediaOwnerType.Certificate,
                            Category = MediaCategory.Certificate,
                            FileUrl = imageUrl,
                        };

                        await _mediaRepository.AddAsync(media, cancellationToken);
                    }
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return OperationResult.Success("Worker register successfully");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                foreach (var url in uploadedUrls)
                {
                    await _blobService.DeleteImageAsync(url);
                }

                throw;
            }
        }

        public async Task<OperationResult> ApproveWorkerRegisterRequest(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken
        )
        {
            var workerRegisterRequest = await _workerProfileRepository.GetByIdAsync(
                id,
                cancellationToken
            );

            if (workerRegisterRequest == null)
            {
                return OperationResult.Failure("Worker register request not found");
            }

            workerRegisterRequest.Status = WorkerStatus.Approved;
            workerRegisterRequest.ApprovedById = userId;
            workerRegisterRequest.ApprovedAt = DateTime.UtcNow;
            workerRegisterRequest.IsOnline = false;
            workerRegisterRequest.IsAcceptingJobs = false;

            if (workerRegisterRequest.User != null)
            {
                workerRegisterRequest.User.IsCitizenIdVerified = true;
                var existingWallet = await _walletRepository.GetByUserIdAsync(
                    workerRegisterRequest.User.Id,
                    WalletOwnerType.Worker,
                    cancellationToken
                );

                if (existingWallet == null)
                {
                    await _walletRepository.AddAsync(
                        new Wallet
                        {
                            UserId = workerRegisterRequest.User.Id,
                            OwnerType = WalletOwnerType.Worker,
                            Balance = 0,
                            LifetimeEarned = 0,
                            LifetimeSpent = 0,
                            CreatedAt = DateTime.UtcNow,
                        },
                        cancellationToken
                    );
                }
            }

            _workerProfileRepository.Update(workerRegisterRequest);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Worker register was approved successfully");
        }

        public async Task<OperationResult> RejectWorkerRegisterRequest(
            Guid id,
            string reason,
            CancellationToken cancellationToken
        )
        {
            var workerRegisterRequest = await _workerProfileRepository.GetByIdAsync(
                id,
                cancellationToken
            );
            if (workerRegisterRequest == null)
            {
                return OperationResult.Failure("Worker register request not found");
            }
            workerRegisterRequest.Status = WorkerStatus.Rejected;
            workerRegisterRequest.RejectReason = reason;
            _workerProfileRepository.Update(workerRegisterRequest);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Worker register was approved successfully");
        }

        public async Task<OperationResult> UpdateWorkerProfileAsync(
            Guid workerId,
            WorkerProfileUpdateRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerProfile = await _workerProfileRepository.GetWorkerProfileDetailByUserIdAsync(
                workerId,
                cancellationToken
            );

            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            var user = workerProfile.User;

            if (user == null)
            {
                return OperationResult.Failure("User not found");
            }

            // Only validate & replace Services if Services are explicitly supplied in DTO
            if (dto.Services != null && dto.Services.Count > 0)
            {
                if (dto.Services.Count is < 1 or > 10)
                {
                    return OperationResult.Failure(
                        "Kĩ thuật viên chỉ được chọn từ 1 đến tối đa 10 dịch vụ."
                    );
                }

                if (dto.Services.Count(x => x.IsPrimary) != 1)
                {
                    return OperationResult.Failure("Vui lòng chọn đúng 1 dịch vụ chính.");
                }
            }

            // =========================
            // Update User Phone
            // =========================
            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                user.Phone = dto.Phone;
                _userRepository.Update(user);
            }

            // =========================
            // Update Worker Profile
            // =========================
            if (dto.Bio != null)
            {
                workerProfile.Bio = dto.Bio;
            }
            if (dto.ExperienceYears.HasValue && dto.ExperienceYears.Value > 0)
            {
                workerProfile.ExperienceYears = dto.ExperienceYears.Value;
            }
            if (dto.MaxDistanceKm.HasValue && dto.MaxDistanceKm.Value > 0)
            {
                workerProfile.MaxDistanceKm = dto.MaxDistanceKm.Value;
            }

            _workerProfileRepository.Update(workerProfile);

            // =========================
            // Update Address
            // =========================
            if (dto.Address != null && (!string.IsNullOrWhiteSpace(dto.Address.City) || !string.IsNullOrWhiteSpace(dto.Address.Detail)))
            {
                var address = await _addressRepository.GetWorkerAddressAsync(
                    workerProfile.Id,
                    cancellationToken
                );

                if (address != null)
                {
                    if (!string.IsNullOrWhiteSpace(dto.Address.City)) address.City = dto.Address.City;
                    if (!string.IsNullOrWhiteSpace(dto.Address.Ward)) address.Ward = dto.Address.Ward;
                    if (!string.IsNullOrWhiteSpace(dto.Address.Detail)) address.Detail = dto.Address.Detail;
                    if (dto.Address.Lat != 0) address.Lat = dto.Address.Lat;
                    if (dto.Address.Lng != 0) address.Lng = dto.Address.Lng;

                    if ((!address.Lat.HasValue || address.Lat == 0) && _goongService != null)
                    {
                        var fullAddr = $"{address.Detail}, {address.Ward}, {address.City}";
                        var (lat, lng) = await _goongService.GeocodeAddressAsync(fullAddr);
                        if (lat.HasValue && lng.HasValue)
                        {
                            address.Lat = lat;
                            address.Lng = lng;
                        }
                    }

                    _addressRepository.Update(address);
                }
            }

            // =========================
            // Replace Services
            // =========================
            if (dto.Services != null && dto.Services.Count > 0)
            {
                _workerServiceRepository.RemoveRange(workerProfile.Services);

                var newServices = dto.Services.Select(x =>
                {
                    var ws = new WorkerService
                    {
                        WorkerProfileId = workerProfile.Id,
                        CategoryId = x.CategoryId,
                        BasePrice = x.BasePrice,
                        IsPrimary = x.IsPrimary,
                    };

                    if (x.Options != null && x.Options.Any())
                    {
                        var sortOrder = 1;
                        foreach (var opt in x.Options)
                        {
                            ws.Options.Add(new WorkerServiceOption
                            {
                                DurationMinutes = opt.DurationMinutes,
                                Price = opt.Price,
                                SortOrder = opt.SortOrder ?? sortOrder++,
                                IsActive = opt.IsActive ?? true
                            });
                        }

                        if (ws.BasePrice <= 0)
                        {
                            ws.BasePrice = ws.Options.Min(o => o.Price);
                        }
                    }

                    return ws;
                }).ToList();

                await _workerServiceRepository.AddRangeAsync(newServices, cancellationToken);
            }
            if (dto.Avatar != null)
            {
                string? newAvatarUrl = null;

                try
                {
                    // upload new avatar
                    newAvatarUrl = await _blobService.UploadImageAsync(dto.Avatar);

                    // get old avatar media
                    var oldAvatarMedia = await _mediaRepository.GetAvatarByUserIdAsync(
                        user.Id,
                        cancellationToken
                    );

                    // delete old blob
                    if (!string.IsNullOrWhiteSpace(user.AvatarUrl))
                    {
                        await _blobService.DeleteImageAsync(user.AvatarUrl);
                    }

                    // remove old media
                    if (oldAvatarMedia != null)
                    {
                        _mediaRepository.Remove(oldAvatarMedia);
                    }

                    // add new media
                    await _mediaRepository.AddAsync(
                        new Media
                        {
                            OwnerId = user.Id,
                            UploadedById = user.Id,
                            OwnerType = MediaOwnerType.User,
                            Category = MediaCategory.Avatar,
                            FileUrl = newAvatarUrl,
                        },
                        cancellationToken
                    );

                    // update user avatar
                    user.AvatarUrl = newAvatarUrl;
                    _userRepository.Update(user);
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(newAvatarUrl))
                    {
                        await _blobService.DeleteImageAsync(newAvatarUrl);
                    }

                    throw;
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult.Success("Update worker profile successfully");
        }

        public async Task<OperationResult> UploadPortfolioImagesAsync(
            Guid workerId,
            UploadPortfolioImagesRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            if (dto.Images.Count == 0)
            {
                return OperationResult.Failure("Please upload at least one image.");
            }

            var currentImages = await _mediaRepository.GetPorfolioImagesByUserId(
                workerId,
                cancellationToken
            );

            if (currentImages.Count + dto.Images.Count > 10)
            {
                return OperationResult.Failure("Maximum 10 portfolio images allowed.");
            }

            var uploadedUrls = new List<string>();

            try
            {
                foreach (var image in dto.Images)
                {
                    var imageUrl = await _blobService.UploadImageAsync(image);

                    uploadedUrls.Add(imageUrl);

                    var media = new Media
                    {
                        OwnerId = workerId,
                        UploadedById = workerId,
                        OwnerType = MediaOwnerType.WorkerProfile,
                        Category = MediaCategory.Portfolio,
                        FileUrl = imageUrl,
                    };

                    await _mediaRepository.AddAsync(media, cancellationToken);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return OperationResult.Success("Upload portfolio images successfully.");
            }
            catch
            {
                foreach (var url in uploadedUrls)
                {
                    await _blobService.DeleteImageAsync(url);
                }

                throw;
            }
        }

        public async Task<OperationResult> DeletePortfolioImageAsync(
            Guid workerId,
            Guid mediaId,
            CancellationToken cancellationToken
        )
        {
            var media = await _mediaRepository.GetByIdAsync(mediaId, cancellationToken);

            if (media == null)
            {
                return OperationResult.Failure("Image not found");
            }

            if (
                media.OwnerId != workerId
                || media.Category != MediaCategory.Portfolio
                || media.OwnerType != MediaOwnerType.WorkerProfile
            )
            {
                return OperationResult.Failure("Forbidden");
            }

            _mediaRepository.Remove(media);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _blobService.DeleteImageAsync(media.FileUrl);

            return OperationResult.Success("Delete portfolio image successfully.");
        }

        public async Task<OperationResult> UpdateIdentificationAsync(
            Guid workerId,
            UpdateIdentificationRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var workerProfile = await _workerProfileRepository.GetWorkerProfileByUserIdAsync(
                workerId,
                cancellationToken
            );

            if (workerProfile == null)
            {
                return OperationResult.Failure("Không tìm thấy hồ sơ kỹ thuật viên.");
            }
            if (dto.Images.Count != 2)
            {
                return OperationResult.Failure(
                    "Vui lòng tải đủ 2 mặt (Mặt trước và Mặt sau) của CCCD."
                );
            }
            if (string.IsNullOrWhiteSpace(dto.CitizenIdNumber))
            {
                return OperationResult.Failure("Vui lòng cung cấp số CCCD định danh.");
            }

            var isCitizenIdDuplicate = await _userRepository.ExistsAsync(
                u => u.CitizenIdNumber == dto.CitizenIdNumber && u.Id != workerProfile.UserId,
                cancellationToken
            );
            if (isCitizenIdDuplicate)
            {
                return OperationResult.Failure("Số CCCD này đã được đăng ký bởi một tài khoản khác.");
            }

            workerProfile.Status = WorkerStatus.Pending;
            if (workerProfile.User != null)
            {
                workerProfile.User.CitizenIdNumber = dto.CitizenIdNumber;
                workerProfile.User.CitizenIdIssuePlace = dto.CitizenIdIssuePlace;
                workerProfile.User.CitizenIdIssueDate = dto.CitizenIdIssueDate;
            }
            var currentImages = await _mediaRepository.GetIdentificateImagesByUserId(
                workerId,
                cancellationToken
            );

            var uploadedUrls = new List<string>();

            try
            {
                var newMedias = new List<Media>();

                foreach (var image in dto.Images)
                {
                    var imageUrl = await _blobService.UploadImageAsync(image);

                    uploadedUrls.Add(imageUrl);

                    newMedias.Add(
                        new Media
                        {
                            OwnerId = workerId,
                            UploadedById = workerId,
                            OwnerType = MediaOwnerType.User,
                            Category = MediaCategory.Identification,
                            FileUrl = imageUrl,
                        }
                    );
                }

                // delete old blob
                foreach (var oldImage in currentImages)
                {
                    await _blobService.DeleteImageAsync(oldImage.FileUrl);
                }

                // remove old db
                foreach (var oldImage in currentImages)
                {
                    _mediaRepository.Remove(oldImage);
                }

                // add new db
                foreach (var media in newMedias)
                {
                    await _mediaRepository.AddAsync(media, cancellationToken);
                }

                if (dto.FaceSelfie != null && workerProfile.User != null)
                {
                    var faceUrl = await _blobService.UploadImageAsync(dto.FaceSelfie);
                    uploadedUrls.Add(faceUrl);

                    // Điểm khớp do máy chủ tự tính, không nhận từ client
                    var faceResult = await ComputeFaceMatchAsync(
                        dto.Images.FirstOrDefault(),
                        dto.FaceSelfie,
                        workerId,
                        cancellationToken
                    );

                    workerProfile.User.FaceImageUrl = faceUrl;
                    workerProfile.User.IsFaceMatched = faceResult.IsMatch;
                    workerProfile.User.FaceMatchScore = faceResult.Score;
                    workerProfile.User.FaceVerifiedAt = faceResult.Score.HasValue
                        ? DateTime.UtcNow
                        : null;
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return OperationResult.Success("Update identification images successfully.");
            }
            catch
            {
                foreach (var url in uploadedUrls)
                {
                    await _blobService.DeleteImageAsync(url);
                }

                throw;
            }
        }

        public async Task<OperationResult> UpdateCentificatesAsync(
            Guid workerId,
            List<WorkerCertificateUploadRequestDto> dto,
            CancellationToken cancellationToken
        )
        {
            var workerProfile = await _workerProfileRepository.GetWorkerProfileDetailByUserIdAsync(
                workerId,
                cancellationToken
            );

            if (workerProfile == null)
            {
                return OperationResult.Failure("Worker profile not found");
            }

            var uploadedUrls = new List<string>();

            try
            {
                var oldCertificates = workerProfile.Certificates.ToList();

                var oldCertificateIds = oldCertificates.Select(x => x.Id).ToList();

                var oldCertificateImages =
                    await _mediaRepository.GetAllWorkerCertificateImagesByCertificateIds(
                        oldCertificateIds,
                        cancellationToken
                    );

                foreach (var image in oldCertificateImages)
                {
                    await _blobService.DeleteImageAsync(image.FileUrl);
                }

                foreach (var image in oldCertificateImages)
                {
                    _mediaRepository.Remove(image);
                }

                _workerCertificateRepository.RemoveRange(oldCertificates);

                foreach (var certificate in dto)
                {
                    var workerCertificate = new WorkerCertificate
                    {
                        WorkerProfileId = workerProfile.Id,
                        Title = certificate.Title,
                        IssuedBy = certificate.IssuedBy,
                    };

                    await _workerCertificateRepository.AddAsync(
                        workerCertificate,
                        cancellationToken
                    );

                    // Upload certificate images

                    foreach (var upload in certificate.MediaUploads)
                    {
                        var imageUrl = await _blobService.UploadImageAsync(upload);

                        uploadedUrls.Add(imageUrl);

                        var media = new Media
                        {
                            OwnerId = workerCertificate.Id,
                            UploadedById = workerId,
                            OwnerType = MediaOwnerType.Certificate,
                            Category = MediaCategory.Certificate,
                            FileUrl = imageUrl,
                        };

                        await _mediaRepository.AddAsync(media, cancellationToken);
                    }
                }

                // pending lại để admin duyệt lại
                workerProfile.Status = WorkerStatus.Pending;

                _workerProfileRepository.Update(workerProfile);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return OperationResult.Success("Update certificates successfully");
            }
            catch
            {
                foreach (var url in uploadedUrls)
                {
                    await _blobService.DeleteImageAsync(url);
                }

                throw;
            }
        }

        public async Task<
            OperationResult<PagedResponse<WorkerProfileDto>>
        > SearchWorkersForCustomerAsync(
            CustomerWorkerSearchQuery query,
            CancellationToken cancellationToken
        )
        {
            var (items, distances, totalCount) =
                await _workerProfileRepository.SearchWorkersForCustomerAsync(
                    query,
                    cancellationToken
                );

            var dtoItems = new List<WorkerProfileDto>();
            for (int i = 0; i < items.Count; i++)
            {
                var worker = items[i];
                var distance = distances[i];

                dtoItems.Add(
                    new WorkerProfileDto
                    {
                        Id = worker.Id,
                        UserId = worker.UserId,
                        FullName = worker.User?.FullName ?? string.Empty,
                        AvatarUrl = worker.User?.AvatarUrl,
                        DateOfBirth = worker.User?.DateOfBirth,
                        Gender = worker.User?.Gender.ToString(),
                        Status = worker.Status.ToString(),
                        ExperienceYears = worker.ExperienceYears,
                        Badge = worker.Badge,
                        RatingAvg = worker.RatingAvg,
                        TotalReviews = worker.TotalReviews,
                        TotalOrders = worker.TotalOrders,
                        IsOnline = worker.IsOnline,
                        IsBusy = worker.IsBusy,
                        DistanceKm = distance.HasValue ? Math.Round(distance.Value, 2) : null,
                        EstimatedArrivalMinutes = distance.HasValue
                            ? (int)Math.Round(distance.Value * 2.5 + 10)
                            : null,
                        City = worker.Address?.City,
                        Services = worker.Services.Select(MapWorkerService).ToList(),
                    }
                );
            }

            return OperationResult<PagedResponse<WorkerProfileDto>>.Success(
                new PagedResponse<WorkerProfileDto>
                {
                    Items = dtoItems,
                    PageNumber = query.PageNumber,
                    PageSize = query.PageSize,
                    TotalCount = totalCount,
                },
                "Search workers successfully"
            );
        }

        //Private method
        private WorkerServiceDto MapWorkerService(WorkerService service)
        {
            return new WorkerServiceDto
            {
                Id = service.Id,
                WorkerProfileId = service.WorkerProfileId,
                CategoryId = service.CategoryId,
                CategoryName = service.Category?.Name,
                BasePrice = service.BasePrice,
                IsPrimary = service.IsPrimary,
                Options = service.Options != null ? service.Options.Select(x => new WorkerServiceOptionDto
                {
                    Id = x.Id,
                    WorkerServiceId = x.WorkerServiceId,
                    DurationMinutes = x.DurationMinutes,
                    Price = x.Price,
                    SortOrder = x.SortOrder,
                    IsActive = x.IsActive
                }).ToList() : new List<WorkerServiceOptionDto>()
            };
        }

        private MediaDto MapMedia(Media media)
        {
            return new MediaDto
            {
                Id = media.Id,
                OwnerId = media.OwnerId,
                FileUrl = media.FileUrl,
            };
        }

        private WorkerCertificateDto MapCertificate(
            WorkerCertificate certificate,
            ILookup<Guid, Media> imageLookup
        )
        {
            return new WorkerCertificateDto
            {
                Id = certificate.Id,
                WorkerProfileId = certificate.WorkerProfileId,
                Title = certificate.Title,
                IssuedAt = certificate.IssuedAt,
                IssuedBy = certificate.IssuedBy,
                CertificateImage = imageLookup[certificate.Id].Select(MapMedia).ToList(),
            };
        }

        private async Task<(
            WorkerProfile WorkerProfile,
            List<Media> PortfolioImages,
            List<Media> IdentificationImages,
            ILookup<Guid, Media> CertificateImageLookup
        )> GetWorkerProfileDataAsync(Guid workerId, CancellationToken cancellationToken)
        {
            var workerProfile = await _workerProfileRepository.GetWorkerProfileDetailByUserIdAsync(
                workerId,
                cancellationToken
            );

            if (workerProfile == null)
            {
                throw new NotFoundException("Worker profile not found");
            }
            if (workerProfile.User == null)
            {
                throw new NotFoundException("Worker user not found");
            }
            var portfolioImages = await _mediaRepository.GetPorfolioImagesByUserId(
                workerProfile.User.Id,
                cancellationToken
            );

            var identificationImages = await _mediaRepository.GetIdentificateImagesByUserId(
                workerProfile.User.Id,
                cancellationToken
            );

            var certificateIds = workerProfile.Certificates.Select(x => x.Id).ToList();

            var workerCertificateImages =
                await _mediaRepository.GetAllWorkerCertificateImagesByCertificateIds(
                    certificateIds,
                    cancellationToken
                );

            return (
                workerProfile,
                portfolioImages,
                identificationImages,
                workerCertificateImages.ToLookup(x => x.OwnerId)
            );
        }

        public async Task<OperationResult> UpdateWorkingStatusAsync(
            Guid workerUserId,
            UpdateWorkingStatusRequestDto dto,
            CancellationToken cancellationToken
        )
        {
            var worker = await _workerProfileRepository.FirstOrDefaultAsync(
                x => x.UserId == workerUserId,
                cancellationToken
            );
            if (worker == null)
            {
                return OperationResult.Failure("Worker profile not found.");
            }

            // Kiểm tra tiền cọc ký quỹ trước khi cho phép bật nhận việc
            if (dto.IsAcceptingJobs.HasValue && dto.IsAcceptingJobs.Value)
            {
                if (!worker.IsDepositPaid)
                {
                    var wallet = await _walletRepository.GetByUserIdAsync(
                        workerUserId,
                        Domain.Enum.WalletOwnerType.Worker,
                        cancellationToken
                    );

                    var currentLocked = wallet?.LockedBalance ?? 0;
                    var required = worker.DepositRequiredAmount;

                    if (currentLocked < required)
                    {
                        return OperationResult.Failure(
                            $"Vui lòng nạp cọc ký quỹ để kích hoạt nhận lịch hẹn Spa. Mức cọc yêu cầu: {required:N0}đ, hiện có: {currentLocked:N0}đ."
                        );
                    }
                }
            }

            if (dto.IsAcceptingJobs.HasValue)
            {
                worker.IsAcceptingJobs = dto.IsAcceptingJobs.Value;
                worker.IsOnline = dto.IsAcceptingJobs.Value;
            }
            if (dto.IsOnline.HasValue)
            {
                worker.IsOnline = dto.IsOnline.Value;
            }

            _workerProfileRepository.Update(worker);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return OperationResult.Success("Update working status successfully.");
        }

        public async Task<OperationResult<WorkerDepositStatusDto>> GetDepositStatusAsync(
            Guid workerUserId,
            CancellationToken cancellationToken
        )
        {
            var worker = await _workerProfileRepository.FirstOrDefaultAsync(
                x => x.UserId == workerUserId,
                cancellationToken
            );

            if (worker == null)
                return OperationResult<WorkerDepositStatusDto>.Failure("Worker profile not found.");

            var wallet = await _walletRepository.GetByUserIdAsync(
                workerUserId,
                Domain.Enum.WalletOwnerType.Worker,
                cancellationToken
            );

            // Tìm dịch vụ Spa chính
            var primaryService = (await _workerServiceRepository.FindAsync(
                ws => ws.WorkerProfileId == worker.Id && ws.IsPrimary,
                cancellationToken
            )).FirstOrDefault();

            var dto = new WorkerDepositStatusDto
            {
                PrimaryServiceId = primaryService?.Id,
                PrimaryServiceName = primaryService?.Category?.Name,
                ServiceCategoryName = primaryService?.Category?.Name,
                DepositRequiredAmount = worker.DepositRequiredAmount > 0
                    ? worker.DepositRequiredAmount
                    : (primaryService?.BasePrice ?? 0),
                LockedBalance = wallet?.LockedBalance ?? 0,
                AvailableBalance = wallet?.Balance ?? 0,
                IsDepositPaid = worker.IsDepositPaid,
                DepositPaidAt = worker.DepositPaidAt,
                CanRequestRefund = worker.IsDepositPaid && !worker.IsOffboardingRequested,
            };

            return OperationResult<WorkerDepositStatusDto>.Success(dto);
        }

        public async Task<OperationResult> RequestOffboardingAsync(
            Guid workerUserId,
            RequestOffboardingDto dto,
            CancellationToken cancellationToken
        )
        {
            var worker = await _workerProfileRepository.FirstOrDefaultAsync(
                x => x.UserId == workerUserId,
                cancellationToken
            );

            if (worker == null)
                return OperationResult.Failure("Worker profile not found.");

            if (!worker.IsDepositPaid)
                return OperationResult.Failure("Bạn chưa nạp tiền cọc ký quỹ hoặc cọc đã được xử lý hoàn trả.");

            if (worker.IsOffboardingRequested)
                return OperationResult.Failure("Yêu cầu ngừng hợp tác và hoàn cọc của bạn đang được xử lý.");

            // 1. Kiểm tra không có ca hẹn nào đang diễn ra
            var activeBooking = await _bookingRepository.GetActiveBookingByWorkerProfileIdAsync(
                worker.Id,
                cancellationToken
            );

            if (activeBooking != null)
            {
                return OperationResult.Failure(
                    "Bạn đang có ca hẹn Spa chưa hoàn thành. Vui lòng hoàn thành hoặc xử lý ca hẹn trước khi yêu cầu ngừng hợp tác."
                );
            }

            // 2. Kiểm tra tài khoản nhận tiền
            var payoutAccount = await _workerPayoutAccountRepository.GetByIdAsync(
                dto.PayoutAccountId,
                cancellationToken
            );

            if (payoutAccount == null || payoutAccount.WorkerProfileId != worker.Id)
            {
                return OperationResult.Failure("Tài khoản ngân hàng nhận tiền hoàn cọc không hợp lệ.");
            }

            // 3. Kiểm tra số dư cọc
            var wallet = await _walletRepository.GetByUserIdAsync(
                workerUserId,
                Domain.Enum.WalletOwnerType.Worker,
                cancellationToken
            );

            if (wallet == null || wallet.LockedBalance <= 0)
            {
                return OperationResult.Failure("Không tìm thấy số dư cọc ký quỹ hợp lệ.");
            }

            await _unitOfWork.BeginTransactionAsync();

            try
            {
                var refundAmount = wallet.LockedBalance;
                var lockedBefore = wallet.LockedBalance;

                // Tạm giữ cọc để chuyển sang PayoutRequest
                wallet.LockedBalance = 0;

                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var random = new Random();
                var payoutCode = "WD" + new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
                var bankCode = payoutAccount.BankCode ?? "";
                var addInfo = Uri.EscapeDataString($"FIXY HOAN COC {payoutCode}");
                var encodedAccountName = Uri.EscapeDataString(payoutAccount.AccountName);
                var vietQrUrl = $"https://img.vietqr.io/image/{bankCode}-{payoutAccount.AccountNumber}-compact2.png?amount={refundAmount}&addInfo={addInfo}&accountName={encodedAccountName}";

                var payoutRequest = new PayoutRequest
                {
                    WorkerProfileId = worker.Id,
                    PayoutAccountId = dto.PayoutAccountId,
                    Amount = refundAmount,
                    Status = PayoutRequestStatus.Pending,
                    PayoutCode = payoutCode,
                    VietQrUrl = vietQrUrl,
                };

                await _payoutRequestRepository.AddAsync(payoutRequest, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var tx = new WalletTransaction
                {
                    WalletId = wallet.Id,
                    PayoutRequestId = payoutRequest.Id,
                    Type = WalletTransactionType.DepositRefund,
                    Direction = WalletDirection.Debit,
                    Amount = refundAmount,
                    BalanceBefore = wallet.Balance,
                    BalanceAfter = wallet.Balance,
                    LockedBalanceBefore = lockedBefore,
                    LockedBalanceAfter = 0,
                    Status = TransactionStatus.Pending,
                    ReferenceId = $"Offboarding refund #{payoutRequest.Id}"
                };

                await _walletTransactionRepository.AddAsync(tx, cancellationToken);
                _walletRepository.Update(wallet);

                // Cập nhật trạng thái KTV
                worker.IsOffboardingRequested = true;
                worker.OffboardingRequestedAt = DateTime.UtcNow;
                worker.IsAcceptingJobs = false;
                worker.IsOnline = false;
                worker.IsDepositPaid = false;

                _workerProfileRepository.Update(worker);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync();

                return OperationResult.Success("Yêu cầu ngừng hợp tác và hoàn 100% tiền cọc ký quỹ đã được gửi thành công. Ban quản trị sẽ chuyển khoản về tài khoản ngân hàng của bạn.");
            }
            catch (DbUpdateConcurrencyException)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return OperationResult.Failure("Xảy ra xung đột dữ liệu ví, vui lòng thử lại.");
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        /// <summary>
        /// So khớp ảnh selfie với ảnh CCCD mặt trước (ảnh đầu tiên trong danh sách giấy tờ).
        /// Lỗi hoặc dịch vụ chưa sẵn sàng => Score = null để admin duyệt tay, không chặn đăng ký.
        /// </summary>
        private async Task<(double? Score, bool IsMatch)> ComputeFaceMatchAsync(
            IFormFile? cardFront,
            IFormFile? selfie,
            Guid userId,
            CancellationToken cancellationToken
        )
        {
            if (cardFront == null || selfie == null)
            {
                _logger.LogWarning(
                    "Không đủ ảnh CCCD mặt trước/selfie để so khớp khuôn mặt cho user {UserId}; cần admin duyệt tay",
                    userId
                );
                return (null, false);
            }

            try
            {
                using var cardStream = new MemoryStream();
                using var selfieStream = new MemoryStream();
                await cardFront.CopyToAsync(cardStream, cancellationToken);
                await selfie.CopyToAsync(selfieStream, cancellationToken);

                var result = await _faceRecognitionService.CompareFacesAsync(
                    cardStream,
                    selfieStream,
                    cancellationToken
                );

                if (!result.IsBothFaceFound)
                {
                    _logger.LogWarning(
                        "So khớp khuôn mặt không cho kết quả cho user {UserId}: {Message}; cần admin duyệt tay",
                        userId,
                        result.Message
                    );
                    return (null, false);
                }

                return (result.Similarity, result.IsMatch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Lỗi khi so khớp khuôn mặt cho user {UserId}; cần admin duyệt tay",
                    userId
                );
                return (null, false);
            }
        }
    }
}
