using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Wintech.Hrms.Api.Modules.Documents;

/// <summary>Verified employee data merged into a generated letter (FR-EMP-072, FR-ADM-082).</summary>
public sealed record LetterPdfModel(
    string LegalEntity,
    string EmployeeName,
    string EmployeeNumber,
    string Designation,
    string Department,
    string Location,
    DateOnly JoinDate,
    string LetterType,
    string Purpose,
    DateTime GeneratedAt);

/// <summary>
/// Server-side letter generation from approved templates with verified merge fields
/// (FR-EMP-072, FR-ADM-082). QuestPDF Community is licensed in Program.cs.
/// </summary>
public static class LetterPdf
{
    public static byte[] Generate(LetterPdfModel model)
    {
        var title = model.LetterType switch
        {
            "SalaryCertificate" => "SALARY CERTIFICATE",
            "ExperienceLetter" => "EXPERIENCE LETTER",
            "AddressProof" => "ADDRESS PROOF LETTER",
            _ => "EMPLOYMENT CERTIFICATE"
        };

        var body = model.LetterType switch
        {
            "SalaryCertificate" =>
                $"This is to certify that {model.EmployeeName} (Employee No. {model.EmployeeNumber}) is employed with {model.LegalEntity} " +
                $"as {model.Designation} in the {model.Department} department since {model.JoinDate:dd MMMM yyyy}.\n\n" +
                $"{model.EmployeeName} has requested this certificate for the purpose of {model.Purpose}. " +
                $"Employment and compensation details shared with this letter are confidential and provided from verified records.",
            "ExperienceLetter" =>
                $"This letter certifies that {model.EmployeeName} (Employee No. {model.EmployeeNumber}) was employed with {model.LegalEntity} " +
                $"as {model.Designation} in the {model.Department} department from {model.JoinDate:dd MMMM yyyy}.\n\n" +
                $"During the tenure, {model.EmployeeName} was based at our {model.Location} location. " +
                $"This letter has been issued for the purpose of {model.Purpose}.",
            "AddressProof" =>
                $"This is to certify that {model.EmployeeName} (Employee No. {model.EmployeeNumber}), {model.Designation} in the {model.Department} department " +
                $"of {model.LegalEntity}, is associated with the organisation since {model.JoinDate:dd MMMM yyyy} and is based at our {model.Location} location.\n\n" +
                $"This letter has been issued for the purpose of {model.Purpose}.",
            _ =>
                $"This is to certify that {model.EmployeeName} (Employee No. {model.EmployeeNumber}) is employed with {model.LegalEntity} " +
                $"as {model.Designation} in the {model.Department} department since {model.JoinDate:dd MMMM yyyy} and is a permanent employee of the organisation.\n\n" +
                $"This certificate has been issued at the request of the employee for the purpose of {model.Purpose}."
        };

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);
                page.DefaultTextStyle(t => t.FontSize(10.5f));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text(model.LegalEntity).Bold().FontSize(14);
                    col.Item().AlignCenter().Text(title).Bold().FontSize(12).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Item().Text($"Date: {model.GeneratedAt:dd MMMM yyyy}");
                    col.Item().PaddingTop(16).Text("To Whom It May Concern,");

                    col.Item().PaddingTop(16).Text(txt =>
                    {
                        foreach (var paragraph in body.Split("\n\n", StringSplitOptions.None))
                        {
                            txt.Span(paragraph);
                            txt.Span("\n\n");
                        }
                    });

                    col.Item().PaddingTop(12).Text("Employee details (from verified records):");
                    col.Item().PaddingLeft(18).Text(txt =>
                    {
                        txt.Span("Name: ").SemiBold(); txt.Span(model.EmployeeName); txt.Span("\n");
                        txt.Span("Employee number: ").SemiBold(); txt.Span(model.EmployeeNumber); txt.Span("\n");
                        txt.Span("Designation: ").SemiBold(); txt.Span(model.Designation); txt.Span("\n");
                        txt.Span("Department: ").SemiBold(); txt.Span(model.Department); txt.Span("\n");
                        txt.Span("Date of joining: ").SemiBold(); txt.Span($"{model.JoinDate:dd MMMM yyyy}");
                    });

                    col.Item().PaddingTop(30).AlignRight().Width(220).Column(sig =>
                    {
                        sig.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        sig.Item().AlignCenter().Text("For " + model.LegalEntity).SemiBold();
                        sig.Item().AlignCenter().Text("Authorised Signatory").FontSize(9);
                    });
                });

                page.Footer().AlignCenter().Text(txt =>
                {
                    txt.Span($"Generated on {model.GeneratedAt:yyyy-MM-dd HH:mm} UTC from verified HR records — computer generated, no signature required.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }
}
