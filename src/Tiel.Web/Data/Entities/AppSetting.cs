namespace Tiel.Web.Data.Entities;

/// <summary>One row of the key-value settings table. Only <c>SettingsService</c> reads or writes it.</summary>
public sealed class AppSetting
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}
