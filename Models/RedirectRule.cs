namespace RequestIntercept.Models;

public class RedirectRule
{
    public required string From { get; set; }
    public required string To { get; set; }
}