using Microsoft.AspNetCore.Components.Forms;
using Relio.Application.People.Import;

namespace Relio.Web.Components.People.Import;

/// <summary>
/// Reads the file the user chose into memory (issue #29), at most <see cref="ImportLimits.MaxFileBytes"/>.
/// The bytes are handed to the parsers and dropped; nothing is stored, and nothing about the file - not
/// even its name - is logged.
/// </summary>
public static class ImportFileReader
{
    /// <summary>Reads <paramref name="file"/>.</summary>
    /// <exception cref="ImportFileException">The file is empty, larger than the limit, or could not be read.</exception>
    public static async Task<byte[]> ReadAsync(IBrowserFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        // The size is reported by the browser, so it is only good for a friendly message: the stream
        // below enforces the limit again. Checked first, nothing is read for a file that is too big.
        if (file.Size == 0)
        {
            throw new ImportFileException(ImportFileProblem.Empty);
        }

        if (file.Size > ImportLimits.MaxFileBytes)
        {
            throw new ImportFileException(ImportFileProblem.TooLarge);
        }

        try
        {
            // maxAllowedSize is always passed: the default is 512,000 bytes and would refuse valid files.
            // The file is streamed in chunks, so the SignalR message limit never comes into it.
            await using var stream = file.OpenReadStream(ImportLimits.MaxFileBytes, cancellationToken);
            using var memory = new MemoryStream(capacity: (int)file.Size);
            await stream.CopyToAsync(memory, cancellationToken);
            if (memory.Length == 0)
            {
                throw new ImportFileException(ImportFileProblem.Empty);
            }

            return memory.ToArray();
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ImportFileException))
        {
            // A dropped connection, a stream refused after the limit, a script error: the exception
            // text is not shown or logged, since it can carry the file name.
            throw new ImportFileException(ImportFileProblem.Unreadable);
        }
    }
}
