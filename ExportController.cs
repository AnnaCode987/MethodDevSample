

namespace DataAccess.Core.Controllers;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using DataAccess.DTO;
using PowerBI.Services;
using PowerBI = Microsoft.PowerBI.Api.Models;
using Serilog;

/// <summary>
/// Represents the controller to import Excel bytes sent from visuals.
/// </summary>
public class ExportController : Controller
{
    private const int RollingDeleteCount = 25;
    private readonly DirectoryInfo tempPathDirectoryInfo;
    private readonly string tempPath;
    private readonly IPowerBIEmbedQueryService powerbiEmbedQueryService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportController" /> class.
    /// </summary>
    /// <param name="powerbiEmbedQueryService">The <see cref="IPowerBIEmbedQueryService"/> service.</param>
    public ExportController(IPowerBIEmbedQueryService powerbiEmbedQueryService)
    {
        this.tempPath = Path.Combine(Path.GetTempPath(), "Phoenix", "ExcelExports");
        this.tempPathDirectoryInfo = new DirectoryInfo(this.tempPath);
        this.powerbiEmbedQueryService = powerbiEmbedQueryService;
    }

    /// <summary>
    /// Imports the Excel bytes into a file after making format changes.
    /// </summary>
    /// <param name="criteria">The Excel export criteria.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    [EnableCors("AnyOrigin")]
    [HttpPost]
    [Authorize(Policy = "Jwt")]
    [Route("{controller}/send")]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Catching all exceptions regardless of the type.")]
    [SuppressMessage("Design", "CA5391:Use antiforgery tokens in ASP.NET Core MVC controllers", Justification = "This method is called from the visuals that is not in a Forms setup with EnableCORS")]
    public async Task<SendFileResult> ImportExcelBytesAsync(string criteria)
    {
        this.CheckWritePaths();
        this.RollingDelete(RollingDeleteCount);
        var request = this.HttpContext.Request;

        if (request.ContentLength > 0)
        {
            var filename = $"{Guid.NewGuid():N}.xlsx";
            var fullFilePath = $"{this.tempPath}\\{filename}";

            try
            {
                using (var stream = new FileStream(fullFilePath, FileMode.Create))
                {
                    await request.BodyReader.CopyToAsync(stream).ConfigureAwait(false);
                }

                FormatCellsInExcel(fullFilePath, criteria);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving the request body to a temp file.");

                return new SendFileResult
                {
                    Success = false
                };
            }

            return new SendFileResult
            {
                ReceivedSize = request.ContentLength,
                Success = true,
                Filename = filename
            };
        }

        return new SendFileResult
        {
            ReceivedSize = 0,
            Success = false
        };
    }

    /// <summary>
    /// Gets the report pages for a report.
    /// </summary>
    /// <param name="reportId">The id for the Power BI report.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    [Route("{controller}/getpages/{reportId}")]
    [HttpGet]
    public async Task<PowerBI.Pages> GetReportPages(string reportId)
    {
        if (this.powerbiEmbedQueryService == null)
        {
            Log.Error($"Controller {nameof(ExportController)} GetReportPages - powerbiEmbedQueryService is null.");

            return null;
        }

        if (Guid.TryParse(reportId, out Guid tempReportGuid))
        {
            return await this.powerbiEmbedQueryService.GetReportPagesAsync(tempReportGuid).ConfigureAwait(false);
        }

        Log.Error($"Controller {nameof(ExportController)} GetReportPages - Report id is null or contains no pages.");

        return null;
    }

    private static void FormatCellsInExcel(string file, string criteria)
    {
        if (string.IsNullOrEmpty(file))
        {
            Log.Error($"Controller {nameof(ExportController)} MergeCellsInExcel - File is empty or null.");
            return;
        }

        if (string.IsNullOrEmpty(criteria))
        {
            Log.Error($"Controller {nameof(ExportController)} MergeCellsInExcel - Criteria is empty or null.");

            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            var excelCriteria = JsonSerializer.Deserialize<ExcelExportCriteria>(criteria, options);

            if (!excelCriteria.Cells.Any())
            {
                Log.Information($"Controller {nameof(ExportController)}  FormatCellsInExcel - No Cells to Format");

                return;
            }

            using var document = SpreadsheetDocument.Open(file, true);

            var workSheet = GetFirstWorksheet(document);

            if (workSheet == null)
            {
                Log.Error($"Controller {nameof(ExportController)} FormatCellsInExcel  - Could not find first worksheet.");
            }

            var styleSheet = GetStylesheet(document);

            foreach (var cell in excelCriteria.Cells)
            {
                FormatHeaderCellInWorksheet(workSheet, styleSheet, cell);
            }

            workSheet.Save();
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
        {
            Log.Error($"Export Controller - Error performing row merge: {ex.Message}");
        }
    }

    private static Worksheet GetFirstWorksheet(SpreadsheetDocument document)
    {
        if (document == null)
        {
            Log.Error($"Controller {nameof(ExportController)} GetFirstWorksheet - Spreadsheet is empty or null.");

            return null;
        }

        IEnumerable<Sheet> sheets = document.WorkbookPart?.Workbook?.Descendants<Sheet>();

        if (!sheets.Any())
        {
            Log.Error($"Controller {nameof(ExportController)} MergeCellsInExcel - File is empty.");

            return null;
        }

        var worksheetPart = (WorksheetPart)document.WorkbookPart?.GetPartById(sheets.FirstOrDefault().Id);

        return worksheetPart.Worksheet;
    }

    private static Stylesheet GetStylesheet(SpreadsheetDocument document)
    {
        if (document == null)
        {
            Log.Error($"Controller {nameof(ExportController)} GetFirstWorksheet - Spreadsheet is empty or null.");

            return null;
        }

        IEnumerable<Sheet> sheets = document.WorkbookPart?.Workbook?.Descendants<Sheet>();

        if (!sheets.Any())
        {
            Log.Error($"Controller {nameof(ExportController)} MergeCellsInExcel - File is empty.");

            return null;
        }

        var stylesheet = document.WorkbookPart?.WorkbookStylesPart?.Stylesheet;
        {
            return stylesheet;
        }
    }

    private static void FormatHeaderCellInWorksheet(Worksheet sheet, Stylesheet stylesheet, ExcelExportCell cellCriteria)
    {
        SheetData sheetData = sheet.GetFirstChild<SheetData>();

        if (sheetData == null)
        {
            Log.Error($"Controller {nameof(ExportController)}  FormatHeaderCellInWorksheet - Sheet data is null");

            return;
        }

        Row row = sheetData.Elements<Row>().Where(r => r.RowIndex == cellCriteria.RowIndex).FirstOrDefault();

        if (row == null)
        {
            Log.Error($"Controller {nameof(ExportController)}  FormatHeaderCellInWorksheet - First row is null");

            return;
        }

        // Identify total column reference - colRefs.Item1 = start, colRefs.Item2 = end
        var (startColRef, endColRef) = GetColRefs(row, cellCriteria);

        // Only merge first column row
        Cell cell = row.Elements<Cell>().Where(c => c.CellReference.Value == $"{startColRef}{cellCriteria.RowIndex}").FirstOrDefault();

        if (cell == null)
        {
            Log.Error($"Controller {nameof(ExportController)}  FormatHeaderCellInWorksheet - {0} cell is null", startColRef);

            return;
        }

        cell.CellValue = new CellValue(cellCriteria.Label);
        cell.DataType = new EnumValue<CellValues>(CellValues.String);

        if (cellCriteria.Bold)
        {
            SetBoldFormat(sheetData, stylesheet, cellCriteria, cell);
        }

        if (cellCriteria.RowSpan == 1)
        {
            Log.Information($"Controller {nameof(ExportController)}  FormatHeaderCellInWorksheet - {0} cell not merged", startColRef);

            return;
        }

        MergeCells mergeCells;

        if (sheet.Elements<MergeCells>().Any())
        {
            if (cellCriteria.ColSpan > 1)
            {
                UnMergeCells(sheet, cellCriteria.RowIndex, cellCriteria.RowSpan, startColRef);
            }

            mergeCells = sheet.Elements<MergeCells>().FirstOrDefault();
        }
        else
        {
            mergeCells = new MergeCells();

            sheet.InsertAfter(mergeCells, sheet.Elements<SheetData>().FirstOrDefault());
        }

        if (mergeCells != null)
        {
            var startRowIndex = cellCriteria.RowIndex;

            var endRowIndex = startRowIndex + cellCriteria.RowSpan - 1;

            mergeCells.Append(new MergeCell() { Reference = new StringValue($"{startColRef}{startRowIndex}:{endColRef}{endRowIndex}") });
        }
    }

    private static void UnMergeCells(Worksheet sheet, int rowStart, int rowSpan, string colRef)
    {
        if (sheet.Elements<MergeCells>().FirstOrDefault() == null)
        {
            Log.Information($"Controller {nameof(ExportController)} UnMergeCells - Sheet Elements MergeCells is null");

            return;
        }

        var rowEnd = rowStart + rowSpan - 1;

        for (var i = rowStart; i <= rowEnd; i++)
        {
            var mergeCells = sheet.Elements<MergeCells>().FirstOrDefault();

            OpenXmlElement target = null;

            foreach (var c in mergeCells)
            {
                var xml = c.OuterXml;
                var cRef = $"{colRef}{i}";

                if (xml.Contains(cRef, StringComparison.InvariantCulture))
                {
                    target = c;

                    Log.Information($"Controller {nameof(ExportController)}  found it");

                    break;
                }
            }

            if (target != null)
            {
                mergeCells.RemoveChild(target);
            }
        }
    }

    private static Tuple<string, string> GetColRefs(OpenXmlElement row, ExcelExportCell criteria)
    {
        var cells = row.Descendants<Cell>().ToArray();

        var startCellRef = cells[criteria.ColIndex].CellReference.ToString();

        var endCellRef = cells[criteria.ColIndex + criteria.ColSpan - 1].CellReference.ToString();

        return new Tuple<string, string>(startCellRef[0..^1], endCellRef[0..^1]);
    }

    private static void SetBoldFormat(SheetData sheetData, Stylesheet stylesheet, ExcelExportCell cellCriteria, Cell cell)
    {
        // get the cellFormat and font for the current cell.
        var origCellFormat = stylesheet.CellFormats.ToList()[(int)cell.StyleIndex.Value] as CellFormat;
        var origFont = stylesheet.Fonts.ToList()[(int)origCellFormat.FontId.Value] as Font;

        if (origFont.Bold == null)
        {
            // get the new indexes
            var fontIndex = stylesheet.Fonts.Count;
            var styleIndex = stylesheet.CellFormats.Count;

            // create a new font from the original with bold text.
            var font = new Font(origFont.OuterXml)
            {
                Bold = new Bold()
            };

            stylesheet.Fonts.Append(font);

            // create a new cellFormat from the original using the new bold font.
            var cellFormat = new CellFormat(origCellFormat.OuterXml)
            {
                FontId = fontIndex
            };

            stylesheet.CellFormats.Append(cellFormat);

            // update the cell styleIndex to the new cellFormat.
            cell.StyleIndex = styleIndex;
        }
    }

    private void CheckWritePaths()
    {
        if (this.tempPathDirectoryInfo.Exists)
        {
            return;
        }

        try
        {
            this.tempPathDirectoryInfo.Create();
        }
        catch (IOException ioException)
        {
            Log.Error($"Export Controller - Error create Excel file directory: {ioException.Message}");
            throw;
        }
    }

    private void RollingDelete(int max)
    {
        var files = this.tempPathDirectoryInfo.GetFiles();

        if (files.Length <= max)
        {
            return;
        }

        var filesByOldest = files.OrderByDescending(m => m.CreationTime).Skip(files.Length - max);

        foreach (var fileInfo in filesByOldest)
        {
            try
            {
                fileInfo.Delete();
            }
            catch (IOException exception)
            {
                Log.Error($"Export Controller - Error performing rolling delete in the Excel file directory: {exception.Message}");
            }
        }
    }
}