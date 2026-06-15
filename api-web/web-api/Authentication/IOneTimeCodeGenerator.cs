namespace web_api.Authentication;

public interface IOneTimeCodeGenerator
{
    string Generate();
}
