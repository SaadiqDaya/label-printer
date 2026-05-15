using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using System.IO;

namespace LabelDesigner.Services;

/// <summary>
/// Creates the 4 default DoorTreats 1x2 label templates on first run.
/// Canvas coordinates are at 96 DPI — displayed at 4x zoom in the designer.
/// Physical size: 50.8 mm × 25.4 mm (2" × 1") for Zebra ZD621.
/// </summary>
public static class TemplateBootstrap
{
    public static void EnsureDefaultTemplates(TemplateService svc)
    {
        CreateIfMissing(svc, DoorTreatsMain());
        CreateIfMissing(svc, DoorTreatsParts());
        CreateIfMissing(svc, DoorTreatsBin());
        CreateIfMissing(svc, Generic1x2());
    }

    private static void CreateIfMissing(TemplateService svc, LabelTemplate t)
    {
        var path = svc.GetDefaultPath(t);
        if (!File.Exists(path)) svc.Save(t, path);
    }

    // ── Template 1: DoorTreats Main ─────────────────────────────────────────
    // Matches: Black White 1 x 2 DoorTreats.btw
    // Fields: itemName, bagType, silverQty, price, qty, unit, bornDate, partSku, barcode
    //
    // Layout (192×96 canvas):
    //  ┌──────────────────────────────────────────────┐
    //  │ [itemName large]               [bagType]     │
    //  │                          [silverQty] SLV G   │
    //  │                          [$price]            │
    //  │ [qty unit]  Born:  [partSku]  [|barcode|]   │
    //  │             [bornDate]                        │
    //  └──────────────────────────────────────────────┘
    private static LabelTemplate DoorTreatsMain() => new()
    {
        Name = "DoorTreats 1x2 Main",
        WidthMm = 50.8, HeightMm = 25.4,
        Fields = ["itemName","bagType","silverQty","price","qty","unit","bornDate","partSku","barcode"],
        Elements =
        [
            new TextElement { X=5,  Y=2,  Width=130, Height=28, FontSize=18, Bold=true,  Text="Product Name",  BoundField="itemName",  ZIndex=0 },
            new TextElement { X=158,Y=2,  Width=30,  Height=14, FontSize=9,              Text="B8",            BoundField="bagType",   ZIndex=0, Alignment=TextAlignmentOption.Right },
            new TextElement { X=130,Y=24, Width=28,  Height=32, FontSize=22, Bold=true,  Text="1",             BoundField="silverQty", ZIndex=0 },
            new TextElement { X=160,Y=32, Width=28,  Height=14, FontSize=8,              Text="SLV G",                                 ZIndex=0 },
            new TextElement { X=122,Y=57, Width=66,  Height=22, FontSize=16, Bold=true,  Text="$4.00",         BoundField="price",     ZIndex=0 },
            new TextElement { X=4,  Y=63, Width=56,  Height=26, FontSize=16, Bold=true,  Text="{{qty}} {{unit}}",                      ZIndex=0 },
            new TextElement { X=63, Y=63, Width=30,  Height=13, FontSize=9,              Text="Born:",                                 ZIndex=0 },
            new TextElement { X=63, Y=76, Width=50,  Height=13, FontSize=9,              Text="4/11/2026",     BoundField="bornDate",  ZIndex=0 },
            new TextElement { X=116,Y=63, Width=42,  Height=13, FontSize=9,              Text="FT20055",       BoundField="partSku",   ZIndex=0 },
            new BarcodeElement { X=159,Y=53, Width=29, Height=40, Format=BarcodeFormatOption.Code128, ShowText=false, BoundField="barcode", BarcodeValue="FT20055", ZIndex=0 }
        ]
    };

    // ── Template 2: DoorTreats Parts ───────────────────────────────────────
    // Matches: Black White 1 x 2 DoorTreats Parts.btw
    // Fields: itemName, qty, unit, bornDate, dtSku, barcode
    //
    //  ┌──────────────────────────────────────────────┐
    //  │                                              │
    //  │ [itemName very large]                        │
    //  │                                              │
    //  │ [qty unit]  Born:  [dtSku]   [|barcode|]   │
    //  │             [bornDate]                       │
    //  └──────────────────────────────────────────────┘
    private static LabelTemplate DoorTreatsParts() => new()
    {
        Name = "DoorTreats 1x2 Parts",
        WidthMm = 50.8, HeightMm = 25.4,
        Fields = ["itemName","qty","unit","bornDate","dtSku","barcode"],
        Elements =
        [
            new TextElement { X=4,  Y=3,  Width=184, Height=50, FontSize=28, Bold=true, Text="Product Name", BoundField="itemName", ZIndex=0 },
            new TextElement { X=4,  Y=62, Width=58,  Height=26, FontSize=16, Bold=true, Text="{{qty}} {{unit}}",                   ZIndex=0 },
            new TextElement { X=66, Y=62, Width=30,  Height=13, FontSize=9,             Text="Born:",                              ZIndex=0 },
            new TextElement { X=66, Y=75, Width=52,  Height=13, FontSize=9,             Text="12/20/2025",   BoundField="bornDate", ZIndex=0 },
            new TextElement { X=122,Y=62, Width=38,  Height=13, FontSize=9,             Text="DT20018",      BoundField="dtSku",    ZIndex=0 },
            new BarcodeElement { X=160,Y=53, Width=28, Height=40, Format=BarcodeFormatOption.Code128, ShowText=false, BoundField="barcode", BarcodeValue="DT20018", ZIndex=0 }
        ]
    };

    // ── Template 3: DoorTreats Bin ─────────────────────────────────────────
    // Matches: Black White 1 x 2 DoorTreats Bin.btw
    // Fields: itemName, bagType, qty, unit, price, ftSku, batchNum, barcode
    //
    //  ┌──────────────────────────────────────────────┐
    //  │           [itemName centered]                │
    //  │ [bagType] [qty unit $price]                  │
    //  │ big       [ftSku]  [batchNum]  [|barcode|]  │
    //  └──────────────────────────────────────────────┘
    private static LabelTemplate DoorTreatsBin() => new()
    {
        Name = "DoorTreats 1x2 Bin",
        WidthMm = 50.8, HeightMm = 25.4,
        Fields = ["itemName","bagType","qty","unit","price","ftSku","batchNum","barcode"],
        Elements =
        [
            new TextElement { X=38, Y=2,  Width=150, Height=18, FontSize=12, Alignment=TextAlignmentOption.Center, Text="Product Name", BoundField="itemName", ZIndex=0 },
            new TextElement { X=4,  Y=20, Width=46,  Height=54, FontSize=30, Bold=true, Text="B1",    BoundField="bagType",  ZIndex=0 },
            new TextElement { X=54, Y=22, Width=100, Height=18, FontSize=12,            Text="{{qty}} {{unit}} ${{price}}",             ZIndex=0 },
            new TextElement { X=54, Y=42, Width=52,  Height=14, FontSize=9,             Text="FT20081",            BoundField="ftSku",   ZIndex=0 },
            new TextElement { X=54, Y=56, Width=52,  Height=14, FontSize=9,             Text="Batch#",             BoundField="batchNum", ZIndex=0 },
            new BarcodeElement { X=152,Y=50, Width=36, Height=42, Format=BarcodeFormatOption.Code128, ShowText=false, BoundField="barcode", BarcodeValue="FT20081", ZIndex=0 }
        ]
    };

    // ── Template 4: Generic 1x2 ────────────────────────────────────────────
    // Matches: Black White 1 x 2.btw  (name-only label)
    private static LabelTemplate Generic1x2() => new()
    {
        Name = "Generic 1x2",
        WidthMm = 50.8, HeightMm = 25.4,
        Fields = ["itemName"],
        Elements =
        [
            new TextElement { X=5, Y=28, Width=182, Height=40, FontSize=20, Text="Item Name", BoundField="itemName",
                              Alignment=TextAlignmentOption.Center, ZIndex=0 }
        ]
    };
}
