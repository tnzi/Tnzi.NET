namespace Tnzi.Template.Entities;

/// <summary>
/// Canonical categorization of a template by rendering surface.
/// Used by the admin UI to filter / tag templates and by the engine
/// to pick sensible default layouts / variable contracts.
/// </summary>
public enum TemplateType
{
    /// <summary>
    /// Generic / unspecified template. Default fallback.
    /// </summary>
    Generic = 0,

    /// <summary>
    /// Email body template (HTML or plain text).
    /// </summary>
    Email = 1,

    /// <summary>
    /// SMS short-message template (plain text, length-constrained).
    /// </summary>
    Sms = 2,

    /// <summary>
    /// Web page / HTML page template.
    /// </summary>
    Page = 3,

    /// <summary>
    /// Print-oriented template (letters, invoices, contracts).
    /// </summary>
    Print = 4,

    /// <summary>
    /// PDF generation template.
    /// </summary>
    Pdf = 5,
}
