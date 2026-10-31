namespace Application.Common.Interfaces
{
    public interface ICurrentUserService
    {
        string? UserId { get; }
        string? UserName { get; }
        string? Email { get; }
        bool IsAuthenticated { get; }

        /// <summary>Người dùng hiện tại có vai trò này không (VD: "ADMIN").</summary>
        bool IsInRole(string role);
    }
}

