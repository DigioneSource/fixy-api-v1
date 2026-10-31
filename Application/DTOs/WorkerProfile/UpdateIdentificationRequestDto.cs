using Microsoft.AspNetCore.Http;

namespace Application.DTOs.WorkerProfile
{
    public class UpdateIdentificationRequestDto
    {
        public string CitizenIdNumber { get; set; } = default!;

        public DateTime CitizenIdIssueDate { get; set; }

        public string CitizenIdIssuePlace { get; set; } = default!;
        public List<IFormFile> Images { get; set; } = [];
        public IFormFile? FaceSelfie { get; set; }
    }
}
