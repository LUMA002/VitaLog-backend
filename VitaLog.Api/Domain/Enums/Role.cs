namespace VitaLog.Api.Domain.Enums;

[Flags]
public enum Role : int
{
    None = 0,
    User = 1 << 0,
    Admin = 1 << 1
}