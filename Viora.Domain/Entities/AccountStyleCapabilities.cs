namespace Viora.Domain.Entities;

public static class AccountStyleCapabilities
{
    public static bool CanCreateArticle(this AccountStyle accountStyle) =>
        accountStyle is AccountStyle.Creator
            or AccountStyle.Journalist
            or AccountStyle.Business
            or AccountStyle.Organization
            or AccountStyle.Agency;
}
