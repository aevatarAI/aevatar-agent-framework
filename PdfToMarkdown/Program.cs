using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run --project PdfToMarkdown <pdf_file> [output_file]");
            Environment.Exit(1);
        }

        string pdfPath = args[0];
        string outputPath = args.Length > 1 ? args[1] : Path.ChangeExtension(pdfPath, ".md");

        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: File not found: {pdfPath}");
            Environment.Exit(1);
        }

        Console.WriteLine($"Extracting text from {pdfPath}...");

        try
        {
            var sb = new StringBuilder();
            using var document = PdfDocument.Open(pdfPath);
            
            int pageNum = 0;
            foreach (Page page in document.GetPages())
            {
                pageNum++;
                var text = page.Text;
                
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Add page separator
                    if (pageNum > 1)
                    {
                        sb.AppendLine();
                        sb.AppendLine("---");
                        sb.AppendLine();
                    }
                    
                    sb.AppendLine($"## Page {pageNum}");
                    sb.AppendLine();
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }

            var markdown = sb.ToString();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                Console.WriteLine("Warning: No text extracted. The PDF might be image-based or encrypted.");
                Environment.Exit(1);
            }

            Console.WriteLine($"Converting to markdown format...");
            
            // Basic markdown formatting improvements
            markdown = FormatAsMarkdown(markdown);

            Console.WriteLine($"Writing to {outputPath}...");
            File.WriteAllText(outputPath, markdown, Encoding.UTF8);

            Console.WriteLine($"✅ Successfully converted PDF to markdown!");
            Console.WriteLine($"   Output: {outputPath}");
            Console.WriteLine($"   Size: {markdown.Length} characters");
            Console.WriteLine($"   Pages: {pageNum}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            Environment.Exit(1);
        }
    }

    static string FormatAsMarkdown(string text)
    {
        var lines = text.Split('\n');
        var result = new StringBuilder();
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (string.IsNullOrEmpty(trimmed))
            {
                result.AppendLine();
                continue;
            }

            // Detect headings (short lines, all caps, or lines ending with :)
            if (trimmed.Length < 80 && (trimmed == trimmed.ToUpper() || trimmed.EndsWith(":")))
            {
                // Check if it's already a heading
                if (!trimmed.StartsWith("#"))
                {
                    result.AppendLine($"## {trimmed}");
                }
                else
                {
                    result.AppendLine(trimmed);
                }
            }
            // Detect numbered lists
            else if (Regex.IsMatch(trimmed, @"^\d+[\.\)]\s+"))
            {
                result.AppendLine(trimmed);
            }
            // Detect bullet points
            else if (Regex.IsMatch(trimmed, @"^[-*•]\s+"))
            {
                result.AppendLine(trimmed);
            }
            else
            {
                result.AppendLine(trimmed);
            }
        }

        return result.ToString();
    }
}
