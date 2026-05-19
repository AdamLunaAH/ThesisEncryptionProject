using System.Net;
namespace CrossCut.Concerns.Extensions;
public static class HttpClientExtensions
{
    public static async Task EnsureSuccessStatusMessage(this HttpResponseMessage response)
    {
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex) when (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var errMessage = await response.Content.ReadAsStringAsync();

            //rethrow with message recieved
            throw new HttpRequestException($"{response.StatusCode}: {errMessage}", ex, ex.StatusCode);
        }
        catch 
        {
            throw;
        }
    }
}