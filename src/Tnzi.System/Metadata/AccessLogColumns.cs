namespace Tnzi.System.Metadata;

/// <summary>
/// <see cref="AccessLog"/> 各字符串列的长度上限。
/// </summary>
/// <remarks>
/// <para>
/// 实体配置（建列）与落库前的截断共用这一份数字，两边各写一份的结果是「列宽改了、截断没跟着改」，而那不报错。
/// </para>
/// <para>
/// ★ 截断不是格式问题而是<b>可用性攻击面</b>：访问日志按批（默认 100 条）用一条 <c>InsertMany</c> 落库，
/// 任何一行超列宽，SQL Server / PostgreSQL 会拒绝整条 INSERT。User-Agent 解析出的版本号与设备型号、
/// IP 归属地库返回的地名，长度都不受任何约束（前者直接取自请求头），于是一个匿名客户端只要发一个
/// 版本号够长的 <c>User-Agent</c>，就能连带抹掉同一批里其他所有人的访问日志，而且可以一直重复。
/// SQLite 不检查长度上限，所以测试全绿证明不了任何事。
/// </para>
/// </remarks>
public static class AccessLogColumns
{
    /// <summary><see cref="AccessLog.Path"/></summary>
    public const int PathMaxLength = 500;

    /// <summary><see cref="AccessLog.Method"/></summary>
    public const int MethodMaxLength = 10;

    /// <summary><see cref="AccessLog.IpAddress"/></summary>
    public const int IpAddressMaxLength = 45;

    /// <summary><see cref="AccessLog.IpCountry"/></summary>
    public const int IpCountryMaxLength = 50;

    /// <summary><see cref="AccessLog.IpProvince"/></summary>
    public const int IpProvinceMaxLength = 50;

    /// <summary><see cref="AccessLog.IpCity"/></summary>
    public const int IpCityMaxLength = 50;

    /// <summary><see cref="AccessLog.IpDistrict"/></summary>
    public const int IpDistrictMaxLength = 50;

    /// <summary><see cref="AccessLog.IpIsp"/></summary>
    public const int IpIspMaxLength = 100;

    /// <summary><see cref="AccessLog.IpFullAddress"/></summary>
    public const int IpFullAddressMaxLength = 200;

    /// <summary><see cref="AccessLog.UaBrowser"/></summary>
    public const int UaBrowserMaxLength = 50;

    /// <summary><see cref="AccessLog.UaBrowserVersion"/></summary>
    public const int UaBrowserVersionMaxLength = 30;

    /// <summary><see cref="AccessLog.UaOperatingSystem"/></summary>
    public const int UaOperatingSystemMaxLength = 50;

    /// <summary><see cref="AccessLog.UaOperatingSystemVersion"/></summary>
    public const int UaOperatingSystemVersionMaxLength = 30;

    /// <summary><see cref="AccessLog.UaDeviceType"/></summary>
    public const int UaDeviceTypeMaxLength = 30;

    /// <summary><see cref="AccessLog.UaDeviceBrand"/></summary>
    public const int UaDeviceBrandMaxLength = 50;

    /// <summary><see cref="AccessLog.UaDeviceModel"/></summary>
    public const int UaDeviceModelMaxLength = 50;

    /// <summary>
    /// 把一个值裁到列宽以内；<c>null</c> 与够短的值原样返回。
    /// </summary>
    /// <remarks>
    /// 刻意不加省略号后缀：后缀会让结果再次超出列宽，而这里唯一的目标就是「装得进去」。
    /// 截断点落在代理对中间时少截一个字符（<see cref="StringExtensions.FitToLength"/>）：孤立的高代理项会让
    /// PostgreSQL 拒绝整批访问日志的 INSERT，与超列宽是同一个后果。
    /// </remarks>
    public static string? Fit(string? value, int maxLength) => value.FitToLength(maxLength);
}
