using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.VibeResearching.Infrastructure.Controllers;

/// <summary>
/// File upload controller for session workspace.
/// </summary>
[ApiController]
[Route("api/sessions/{sessionId}")]
public class UploadController : AbpControllerBase
{
    private readonly IWorkspaceAppService _workspaceAppService;

    public UploadController(IWorkspaceAppService workspaceAppService)
    {
        _workspaceAppService = workspaceAppService;
    }

    /// <summary>
    /// Uploads a file with optional extraction/processing.
    /// POST /api/sessions/{sessionId}/uploads/extract
    /// </summary>
    [HttpPost("uploads/extract")]
    public async Task<IActionResult> UploadWithExtractionAsync(
        [FromRoute] string sessionId,
        [FromForm] IFormFile file,
        [FromForm] bool? extract,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "No file provided or file is empty" });
        }

        // Create a temporary file to save the upload
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var fileName = file.FileName;

        try
        {
            // Save the uploaded file to temp location
            await using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, ct);
            }

            // Read the file content
            var content = await System.IO.File.ReadAllTextAsync(tempPath, ct);

            // Save to workspace
            await _workspaceAppService.SaveFileAsync(
                sessionId,
                new SaveFileDto
                {
                    Path = fileName,
                    Content = content
                },
                ct);

            // TODO: If extract flag is true, perform extraction/processing
            // This would involve parsing the file content and extracting structured data
            // For now, we just upload the file as-is

            return Ok(new
            {
                ok = true,
                sessionId,
                file = new
                {
                    name = fileName,
                    path = fileName,
                    size = file.Length,
                    extracted = extract ?? false
                }
            });
        }
        finally
        {
            // Clean up temp file
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Uploads multiple files to the session workspace.
    /// POST /api/sessions/{sessionId}/uploads
    /// </summary>
    [HttpPost("uploads")]
    public async Task<IActionResult> UploadFilesAsync(
        [FromRoute] string sessionId,
        [FromForm] List<IFormFile> files,
        CancellationToken ct = default)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { error = "No files provided" });
        }

        var uploadedPaths = new List<string>();

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                continue;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                // Save the uploaded file to temp location
                await using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, ct);
                }

                // Read the file content
                var content = await System.IO.File.ReadAllTextAsync(tempPath, ct);

                // Save to workspace
                await _workspaceAppService.SaveFileAsync(
                    sessionId,
                    new SaveFileDto
                    {
                        Path = file.FileName,
                        Content = content
                    },
                    ct);

                uploadedPaths.Add(file.FileName);
            }
            finally
            {
                // Clean up temp file
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }

        return Ok(new
        {
            ok = true,
            sessionId,
            files = uploadedPaths,
            count = uploadedPaths.Count
        });
    }
}
