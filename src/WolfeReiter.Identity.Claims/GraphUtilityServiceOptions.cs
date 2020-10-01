
namespace WolfeReiter.Identity.Claims
{
    public class GraphUtilityServiceOptions
    {
        public string GraphApiVersion { get; set; } = "https://graph.microsoft.com/v1.0";
        public string GraphEndpoint { get; set; }  = "https://graph.microsoft.com/.default";
    }
}