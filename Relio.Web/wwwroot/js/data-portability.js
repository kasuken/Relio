export async function downloadStream(filename, contentType, streamReference) {
    const buffer = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([buffer], { type: contentType }));
    const link = document.createElement("a");
    let downloadStarted = false;

    try {
        link.href = url;
        link.download = filename;
        link.hidden = true;
        document.body.appendChild(link);
        link.click();
        downloadStarted = true;
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } finally {
        link.remove();
        if (!downloadStarted) {
            URL.revokeObjectURL(url);
        }
    }
}
