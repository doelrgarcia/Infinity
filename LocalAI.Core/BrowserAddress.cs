namespace LocalAI.Core;
public static class BrowserAddress
{
    public static string Normalize(string value)
    {
        value=value.Trim();if(value.Length is 0 or >4096||value.Any(char.IsControl))throw new InvalidDataException("Enter a web address.");
        if(!value.Contains("://")&&!value.Contains(':'))value="https://"+value;
        if(value.StartsWith("localhost:",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(value,@"^127\.0\.0\.1:\d"))value="http://"+value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http")||string.IsNullOrWhiteSpace(uri.Host)||uri.UserInfo.Length!=0)throw new InvalidDataException("Use an HTTP or HTTPS address without embedded passwords.");
        return uri.AbsoluteUri;
    }
}
