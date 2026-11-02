using System.IO;
using System.Windows;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using static PptSlider.Ooxml;
using static PptSlider.SlideTree;

namespace PptSlider;

/// <summary>New shapes on a slide: pictures, text boxes, and the shapes of the Shapes gallery (tables: <see cref="Tables"/>).</summary>
static class ShapeFactory
{
    /// <summary>Picture formats <see cref="AddPicture"/> takes, by file extension.</summary>
    public static PartTypeInfo? PictureType(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => ImagePartType.Png,
        ".jpg" or ".jpeg" => ImagePartType.Jpeg,
        ".gif" => ImagePartType.Gif,
        ".bmp" => ImagePartType.Bmp,
        _ => null,
    };

    /// <summary>Adds a picture shape showing <paramref name="image"/> at <paramref name="r"/> (slide coordinates).</summary>
    public static OpenXmlElement AddPicture(SlidePart sp, byte[] image, PartTypeInfo type, Rect r)
    {
        var img = sp.AddImagePart(type);
        using (var s = img.GetStream(FileMode.Create)) s.Write(image);
        var pic = new P.Picture($"""
            <p:pic {Ns}><p:nvPicPr><p:cNvPr id="{NextId(sp)}" name="Picture"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>
            <p:blipFill><a:blip r:embed="{sp.GetIdOfPart(img)}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>
            <p:spPr>{Xfrm(r)}<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr></p:pic>
            """);
        AppendShape(sp, pic);
        return pic;
    }

    public static OpenXmlElement AddTextBox(SlidePart sp, Rect r, string text)
    {
        var shape = new P.Shape($"""
            <p:sp {Ns}><p:nvSpPr><p:cNvPr id="{NextId(sp)}" name="TextBox"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
            <p:spPr>{Xfrm(r)}<a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>
            <p:txBody><a:bodyPr wrap="square"><a:spAutoFit/></a:bodyPr><a:lstStyle/><a:p><a:r><a:rPr lang="ko-KR" sz="2400"/><a:t></a:t></a:r></a:p></p:txBody></p:sp>
            """);
        shape.Descendants<A.Text>().First().Text = text;
        AppendShape(sp, shape);
        return shape;
    }

    /// <summary>The shapes the Shapes gallery offers: preset geometry names, plus "line" and "arrow".</summary>
    public static readonly string[] ShapeKinds =
        ["rect", "roundRect", "ellipse", "triangle", "rtTriangle", "diamond", "parallelogram", "trapezoid", "pentagon", "hexagon",
         "homePlate", "chevron", "star5", "rightArrow", "leftArrow", "upArrow", "downArrow", "line", "arrow"];

    static string Xfrm(Rect r, bool flipH = false, bool flipV = false) =>
        $"""<a:xfrm{(flipH ? " flipH=\"1\"" : "")}{(flipV ? " flipV=\"1\"" : "")}><a:off x="{Emu(r.X)}" y="{Emu(r.Y)}"/><a:ext cx="{Emu(r.Width)}" cy="{Emu(r.Height)}"/></a:xfrm>""";

    /// <summary>
    /// Adds a shape as PowerPoint draws a new one: theme accent fill and outline, white centered text.
    /// A line or arrow runs from one corner of <paramref name="r"/> to the other; the flips say which.
    /// </summary>
    public static OpenXmlElement AddShape(SlidePart sp, string kind, Rect r, bool flipH = false, bool flipV = false)
    {
        uint id = NextId(sp);
        OpenXmlElement el;
        if (kind is "line" or "arrow")
            el = new P.ConnectionShape($"""
                <p:cxnSp {Ns}><p:nvCxnSpPr><p:cNvPr id="{id}" name="{(kind == "arrow" ? "Straight Arrow Connector" : "Straight Connector")} {id - 1}"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr>
                <p:spPr>{Xfrm(r, flipH, flipV)}<a:prstGeom prst="{(kind == "arrow" ? "straightConnector1" : "line")}"><a:avLst/></a:prstGeom><a:ln w="19050">{(kind == "arrow" ? "<a:tailEnd type=\"triangle\"/>" : "")}</a:ln></p:spPr>
                <p:style><a:lnRef idx="1"><a:schemeClr val="accent1"/></a:lnRef><a:fillRef idx="0"><a:schemeClr val="accent1"/></a:fillRef><a:effectRef idx="0"><a:schemeClr val="accent1"/></a:effectRef><a:fontRef idx="minor"><a:schemeClr val="tx1"/></a:fontRef></p:style></p:cxnSp>
                """);
        else
            el = new P.Shape($"""
                <p:sp {Ns}><p:nvSpPr><p:cNvPr id="{id}" name="Shape {id - 1}"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
                <p:spPr>{Xfrm(r, flipH, flipV)}<a:prstGeom prst="{kind}"><a:avLst/></a:prstGeom></p:spPr>
                <p:style><a:lnRef idx="2"><a:schemeClr val="accent1"><a:shade val="15000"/></a:schemeClr></a:lnRef><a:fillRef idx="1"><a:schemeClr val="accent1"/></a:fillRef><a:effectRef idx="0"><a:schemeClr val="accent1"/></a:effectRef><a:fontRef idx="minor"><a:schemeClr val="lt1"/></a:fontRef></p:style>
                <p:txBody><a:bodyPr rtlCol="0" anchor="ctr"/><a:lstStyle/><a:p><a:pPr algn="ctr"/><a:endParaRPr lang="ko-KR"/></a:p></p:txBody></p:sp>
                """);
        AppendShape(sp, el);
        return el;
    }
}
