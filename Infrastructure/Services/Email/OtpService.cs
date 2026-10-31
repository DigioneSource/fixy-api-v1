using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Application.Common;
using Application.DTOs.Auth;
using Application.Common.Models.Email;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services.Email;
using Application.Interfaces.Services.Sms;
using Domain.Entity;
using Domain.Enum;
using Infrastructure.Helpers;

namespace Infrastructure.Services.Email
{
    public class OtpService : IOtpService
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserOtpRepository _userOtpRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailQueue _emailQueue;
        private readonly ITemplateEngine _templateEngine;
        private readonly ISmsService _smsService;

        public OtpService(
            IUserRepository userRepository,
            IUserOtpRepository userOtpRepository,
            IUnitOfWork unitOfWork,
            IEmailQueue emailQueue,
            ITemplateEngine templateEngine,
            ISmsService smsService
        )
        {
            _userRepository = userRepository;
            _userOtpRepository = userOtpRepository;
            _unitOfWork = unitOfWork;
            _emailQueue = emailQueue;
            _templateEngine = templateEngine;
            _smsService = smsService;
        }

        public async Task<OperationResult> SendOtpAsync(
            string target,
            EmailPurpose purpose,
            CancellationToken cancellationToken
        )
        {
            target = NormalizeTarget(target);

            return await SendOtpInternalAsync(target, purpose, cancellationToken);
        }

        public async Task<OperationResult> VerifyOtpAsync(
            string target,
            string otpCode,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(otpCode))
            {
                return OperationResult.Failure("Mã OTP không hợp lệ");
            }

            target = NormalizeTarget(target);

            // Lấy mã mới nhất theo target (không lọc theo code) để đếm được lần nhập sai
            var otp = await _userOtpRepository.GetLatestPendingOtpAsync(target, cancellationToken);

            if (otp == null)
            {
                return OperationResult.Failure("Mã OTP không hợp lệ hoặc đã được sử dụng");
            }

            if (otp.ExpiryDate < DateTime.UtcNow)
            {
                otp.IsUsed = true;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return OperationResult.Failure("Mã OTP đã hết hạn, vui lòng yêu cầu mã mới");
            }

            if (!CodeEquals(otp.OtpCode, otpCode.Trim()))
            {
                otp.FailedAttempts++;

                if (otp.FailedAttempts >= OtpConstants.MaxFailedAttempts)
                {
                    otp.IsUsed = true;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return OperationResult.Failure(
                        $"Bạn đã nhập sai quá {OtpConstants.MaxFailedAttempts} lần, vui lòng yêu cầu mã mới"
                    );
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var remaining = OtpConstants.MaxFailedAttempts - otp.FailedAttempts;
                return OperationResult.Failure($"Mã OTP không đúng, bạn còn {remaining} lần thử");
            }

            var verifyToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

            otp.IsUsed = true;
            otp.IsVerified = true;
            otp.VerifiedAt = DateTime.UtcNow;
            otp.VerifyTokenHash = TokenHasher.Hash(verifyToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new OperationResult
            {
                IsSuccess = true,
                Message = "OTP verified successfully",
                Data = new VerifyOtpResponseDto
                {
                    VerifyToken = verifyToken,
                    ExpiresInSeconds = OtpConstants.VerifyTokenLifetimeMinutes * 60,
                },
            };
        }

        private static bool CodeEquals(string expected, string actual)
        {
            var a = Encoding.UTF8.GetBytes(expected);
            var b = Encoding.UTF8.GetBytes(actual);

            // FixedTimeEquals trả false ngay nếu khác độ dài; độ dài mã OTP cố định nên không lộ thông tin
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private async Task<OperationResult> SendOtpInternalAsync(
            string target,
            EmailPurpose purpose,
            CancellationToken cancellationToken
        )
        {
            if (!IsEmail(target) && !IsPhone(target))
            {
                return OperationResult.Failure("Invalid email or phone number");
            }

            var latestOtp = await _userOtpRepository.GetLatestOtpByTargetAsync(
                target,
                cancellationToken
            );

            if (latestOtp != null)
            {
                var nextRequestTime = latestOtp.CreatedDate.AddSeconds(60);

                if (nextRequestTime > DateTime.UtcNow)
                {
                    var remain = (int)(nextRequestTime - DateTime.UtcNow).TotalSeconds;

                    return OperationResult.Failure(
                        $"Please wait {remain}s before requesting another OTP"
                    );
                }
            }

            var existedUser = await _userRepository.GetByTargetAsync(target, cancellationToken);

            switch (purpose)
            {
                case EmailPurpose.Register:

                    if (existedUser != null)
                    {
                        return OperationResult.Failure("Email or phone number already used");
                    }

                    break;

                case EmailPurpose.ForgotPassword:

                    if (existedUser == null)
                    {
                        return OperationResult.Failure("User not found");
                    }

                    break;
            }

            var oldOtps = await _userOtpRepository.GetUnusedOtpsAsync(target, cancellationToken);

            foreach (var otp in oldOtps)
            {
                otp.IsUsed = true;
            }

            var otpCode = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

            var entity = new UserOtp
            {
                Target = target,
                OtpCode = otpCode,
                Purpose = purpose,
                CreatedDate = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddMinutes(5),
                IsUsed = false,
                IsVerified = false,
            };

            await _userOtpRepository.AddAsync(entity, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (IsEmail(target))
            {
                var template = purpose switch
                {
                    EmailPurpose.Register => "RegisterOtpTemplate",

                    EmailPurpose.ForgotPassword => "ForgotPasswordOtpTemplate",

                    _ => "RegisterOtpTemplate",
                };

                var html = await _templateEngine.RenderEmailTemplateAsync(
                    template,
                    new OtpEmailModel { Otp = otpCode }
                );

                await _emailQueue.QueueEmailAsync(
                    new EmailMessage
                    {
                        To = target,
                        Subject = "Mã OTP của bạn",
                        Body = html,
                    }
                );
            }

            if (IsPhone(target))
            {
                var smsContent = $"[FIXY] Ma OTP xac thuc cua ban la: {otpCode}. Ma co hieu luc trong 5 phut. Vui long khong chia se ma nay voi ai.";
                await _smsService.SendSmsAsync(target, smsContent, cancellationToken);
            }

            return OperationResult.Success("OTP sent successfully");
        }

        private string NormalizeTarget(string target)
        {
            return target.Trim().ToLower();
        }

        private bool IsEmail(string target)
        {
            return Regex.IsMatch(target, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private bool IsPhone(string target)
        {
            return Regex.IsMatch(target, @"^\d{9,11}$");
        }
    }
}
