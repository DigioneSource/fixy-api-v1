using Application.Common;
using Application.Interfaces;
using Application.Interfaces.Repositories;
using Application.Interfaces.Services;
using Domain.Entity;
using Domain.Enum;

namespace Application.Services
{
    public class WorkerWeeklyScheduleService : IWorkerWeeklyScheduleService
    {
        private readonly IWorkerWeeklyScheduleRepository _workerWeeklyScheduleRepository;
        private readonly IUnitOfWork _unitOfWork;

        public WorkerWeeklyScheduleService(
            IWorkerWeeklyScheduleRepository workerWeeklyScheduleRepository,
            IUnitOfWork unitOfWork
        )
        {
            _workerWeeklyScheduleRepository = workerWeeklyScheduleRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<OperationResult<List<WorkerWeeklySchedule>>> GetWeeklySchedulesAsync(
            Guid workerProfileId,
            CancellationToken cancellationToken = default
        )
        {
            var schedules = await _workerWeeklyScheduleRepository.GetByWorkerProfileIdAsync(
                workerProfileId,
                cancellationToken
            );

            return OperationResult<List<WorkerWeeklySchedule>>.Success(
                schedules,
                "Get schedules successfully"
            );
        }

        public async Task<OperationResult> CreateDefaultScheduleAsync(
            Guid workerProfileId,
            bool saveChanges = true,
            CancellationToken cancellationToken = default
        )
        {
            var existed = await _workerWeeklyScheduleRepository.AnyByWorkerAsync(
                workerProfileId,
                cancellationToken
            );

            if (existed)
            {
                return OperationResult.Failure("Default schedule already exists");
            }
            var schedules = new List<WorkerWeeklySchedule>
            {
                CreateSchedule(workerProfileId, WeekDay.Mon),
                CreateSchedule(workerProfileId, WeekDay.Tue),
                CreateSchedule(workerProfileId, WeekDay.Wed),
                CreateSchedule(workerProfileId, WeekDay.Thu),
                CreateSchedule(workerProfileId, WeekDay.Fri),
                // 21/11/2026: dịch vụ tại nhà đông nhất cuối tuần & buổi tối → mặc định làm cả tuần.
                CreateSchedule(workerProfileId, WeekDay.Sat),
                CreateSchedule(workerProfileId, WeekDay.Sun),
            };

            await _workerWeeklyScheduleRepository.AddRangeAsync(schedules);

            if (saveChanges)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return OperationResult.Success("Create default schedule successfully");
        }

        public async Task<OperationResult> UpdateWeeklyScheduleAsync(
            Guid workerProfileId,
            WeekDay dayOfWeek,
            TimeOnly? startTime,
            TimeOnly? endTime,
            bool isActive,
            CancellationToken cancellationToken = default
        )
        {
            var schedule = await _workerWeeklyScheduleRepository.GetByWorkerAndDayAsync(
                workerProfileId,
                dayOfWeek,
                cancellationToken
            );

            if (schedule == null)
            {
                return OperationResult.Failure("Schedule not found");
            }

            schedule.IsActive = isActive;
            if (!isActive)
            {
                schedule.StartTime = null;
                schedule.EndTime = null;
            }
            else
            {
                if (startTime == null || endTime == null)
                {
                    return OperationResult.Failure("StartTime and EndTime are required");
                }

                if (startTime >= endTime)
                {
                    return OperationResult.Failure("StartTime must be earlier than EndTime");
                }
                schedule.StartTime = startTime;
                schedule.EndTime = endTime;
            }

            _workerWeeklyScheduleRepository.Update(schedule);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return OperationResult.Success("Update schedule successfully");
        }

        private static WorkerWeeklySchedule CreateSchedule(Guid workerProfileId, WeekDay day)
        {
            return new WorkerWeeklySchedule
            {
                WorkerProfileId = workerProfileId,
                DayOfWeek = day,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(22, 0),
                IsActive = true,
            };
        }
    }
}
