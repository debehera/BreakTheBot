using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BreakTheBot.Web.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;
    public string Heading { get; private set; } = "";
    public string Message { get; private set; } = "";
    public string? RequestId { get; private set; }

    public void OnGet(int? code)
    {
        Code = code is >= 400 and <= 599 ? code.Value : 500;

        (Heading, Message) = Code switch
        {
            404 => ("Page not found",
                    "We could not find that page. It may have moved, or the address may have a typo."),
            403 => ("Access denied",
                    "You do not have permission to open that page."),
            401 => ("Please log in",
                    "You need to be logged in to see that page."),
            429 => ("Too many requests",
                    "You are going a bit fast. Wait a moment and try again."),
            >= 400 and < 500 => ("That request did not work",
                    "Something was wrong with the request. Please go back and try again."),
            _ => ("Something went wrong",
                  "An unexpected error happened on our side. It has been logged. Please try again in a moment.")
        };

        // A short reference id only. Never expose exception details or stack traces to visitors.
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    // Errors on POST requests are re-run with the same method, so handle it too.
    public void OnPost(int? code) => OnGet(code);
}