using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Wintech.Hrms.Api.Modules.Payroll;

namespace Wintech.Hrms.Api.Modules.Payroll;

/// <summary>Content required to render a payslip PDF (contract §10 download endpoint).</summary>
public sealed record PayslipPdfModel(
    string LegalEntity,
    string EmployeeNumber,
    string EmployeeName,
    string Department,
    string Designation,
    string PeriodLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<PayslipComponentDto> Earnings,
    IReadOnlyList<PayslipComponentDto> Deductions,
    decimal Gross,
    decimal TotalDeductions,
    decimal NetPay,
    string Currency,
    DateTime GeneratedAt);

/// <summary>
/// Server-side payslip PDF generation (FR-EMP-061/062). QuestPDF Community is licensed in
/// Program.cs; documents are generated from verified database data, never from client input.
/// </summary>
public static class PayslipPdf
{
    public static byte[] Generate(PayslipPdfModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(model.LegalEntity).Bold().FontSize(15);
                    col.Item().Text($"Payslip — {model.PeriodLabel}").FontSize(12).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        void Cell(string label, string value) =>
                            t.Cell().PaddingBottom(2).Text(txt =>
                            {
                                txt.Span(label + ": ").SemiBold();
                                txt.Span(value);
                            });

                        Cell("Employee", $"{model.EmployeeName} ({model.EmployeeNumber})");
                        Cell("Period", $"{model.StartDate:yyyy-MM-dd} to {model.EndDate:yyyy-MM-dd}");
                        Cell("Designation", model.Designation);
                        Cell("Department", model.Department);
                    });

                    col.Item().PaddingTop(10).Text("Earnings").Bold().FontSize(11);
                    col.Item().PaddingTop(2).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Component").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Amount").Bold();
                        });

                        foreach (var earning in model.Earnings)
                        {
                            t.Cell().Padding(4).Text(earning.Component);
                            t.Cell().Padding(4).AlignRight().Text(FormatMoney(earning.Amount, model.Currency));
                        }

                        t.Cell().Padding(4).Background(Colors.Grey.Lighten2).Text("Gross").Bold();
                        t.Cell().Padding(4).AlignRight().Background(Colors.Grey.Lighten2)
                            .Text(FormatMoney(model.Gross, model.Currency)).Bold();
                    });

                    col.Item().PaddingTop(10).Text("Deductions").Bold().FontSize(11);
                    col.Item().PaddingTop(2).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Component").Bold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignRight().Text("Amount").Bold();
                        });

                        foreach (var deduction in model.Deductions)
                        {
                            t.Cell().Padding(4).Text(deduction.Component);
                            t.Cell().Padding(4).AlignRight().Text(FormatMoney(deduction.Amount, model.Currency));
                        }

                        t.Cell().Padding(4).Background(Colors.Grey.Lighten2).Text("Total deductions").Bold();
                        t.Cell().Padding(4).AlignRight().Background(Colors.Grey.Lighten2)
                            .Text(FormatMoney(model.TotalDeductions, model.Currency)).Bold();
                    });

                    col.Item().PaddingTop(12).Background(Colors.Grey.Lighten3).Padding(8).Row(row =>
                    {
                        row.RelativeItem().Text("Net pay").Bold().FontSize(12);
                        row.ConstantItem(160).AlignRight()
                            .Text(FormatMoney(model.NetPay, model.Currency)).Bold().FontSize(12);
                    });
                });

                page.Footer().PaddingTop(10).Column(col =>
                {
                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                    col.Item().PaddingTop(4).AlignCenter().Text(txt =>
                    {
                        txt.Span("System generated payslip — no signature required. Downloaded copy is time-bound and audited (FR-EMP-062).").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    col.Item().AlignCenter().Text(txt =>
                    {
                        txt.Span($"Generated at {model.GeneratedAt:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string FormatMoney(decimal amount, string currency) => $"{currency} {amount:N2}";
}
