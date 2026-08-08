using System.IO.Compression;
using System.Text;

namespace StoryVoice.Tests.Shared;

internal static class MinimalEpub
{
    public static byte[] Create()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            Add(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" />
                  </rootfiles>
                </container>
                """);
            Add(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="UTF-8"?>
                <package version="3.0" unique-identifier="book-id" xmlns="http://www.idpf.org/2007/opf">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">storyvoice-test</dc:identifier>
                    <dc:title>月下 EPUB</dc:title>
                    <dc:creator>StoryVoice</dc:creator>
                    <dc:language>zh-TW</dc:language>
                  </metadata>
                  <manifest>
                    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav" />
                    <item id="chapter-1" href="chapter-1.xhtml" media-type="application/xhtml+xml" />
                    <item id="chapter-2" href="chapter-2.xhtml" media-type="application/xhtml+xml" />
                  </manifest>
                  <spine>
                    <itemref idref="chapter-1" />
                    <itemref idref="chapter-2" />
                  </spine>
                </package>
                """);
            Add(archive, "OEBPS/nav.xhtml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
                  <head><title>目錄</title></head>
                  <body><nav epub:type="toc"><ol>
                    <li><a href="chapter-1.xhtml">第一章 月影</a></li>
                    <li><a href="chapter-2.xhtml">第二章 火蝶</a></li>
                  </ol></nav></body>
                </html>
                """);
            Add(archive, "OEBPS/chapter-1.xhtml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml"><head><title>第一章 月影</title></head>
                <body><h1>第一章 月影</h1><p>月色落在窗前。</p><p>故事開始了。</p></body></html>
                """);
            Add(archive, "OEBPS/chapter-2.xhtml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml"><head><title>第二章 火蝶</title></head>
                <body><h1>第二章 火蝶</h1><p>火蝶飛向彼岸。</p></body></html>
                """);
        }

        return output.ToArray();
    }

    private static void Add(
        ZipArchive archive,
        string path,
        string content,
        CompressionLevel compression = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(path, compression);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}
