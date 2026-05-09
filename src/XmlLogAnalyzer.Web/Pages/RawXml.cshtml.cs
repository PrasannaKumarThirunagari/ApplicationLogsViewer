using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace XmlLogAnalyzer.Web.Pages;

// Page removed — kept as a redirect for any stale bookmarks.
public class RawXmlModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");
}
