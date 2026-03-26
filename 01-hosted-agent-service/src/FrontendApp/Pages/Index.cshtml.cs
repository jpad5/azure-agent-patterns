using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;

namespace FrontendApp.Pages;

[AuthorizeForScopes(ScopeKeySection = "AgentService:Scope")]
public class IndexModel : PageModel
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IConfiguration _configuration;

    public string UserName { get; private set; } = string.Empty;
    public string AccessToken { get; private set; } = string.Empty;
    public string AgentServiceBaseUrl { get; private set; } = string.Empty;

    public IndexModel(ITokenAcquisition tokenAcquisition, IConfiguration configuration)
    {
        _tokenAcquisition = tokenAcquisition;
        _configuration = configuration;
    }

    public async Task OnGetAsync()
    {
        UserName = User.Identity?.Name ?? "Unknown";
        AgentServiceBaseUrl = _configuration["AgentService:BaseUrl"]!;

        var scope = _configuration["AgentService:Scope"]!;
        AccessToken = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { scope });
    }
}
