namespace Ci_Cd.Models
{
    public class UploaderOptions
    {
        public int RetryAttempts { get; set; } = 4;
        public int BaseDelaySeconds { get; set; } = 2;
        public int HttpTimeoutSeconds { get; set; } = 300; // 5 minutes
        public int MaxFileSizeMb { get; set; } = 100; // max file size allowed for upload
    }
}
