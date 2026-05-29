namespace LabelDesigner.Core.Models;

public enum ElementType { Text, Barcode, Image, Rectangle, Ellipse, Line, Table }

// New formats are appended so existing string-serialized templates keep mapping correctly.
public enum BarcodeFormatOption { Code128, QRCode, EAN13, UPCA, DataMatrix, PDF417, Code39, ITF, Codabar, Code93, GS1_128, Aztec }

public enum TextAlignmentOption { Left, Center, Right }

public enum ShapeType { Rectangle, Ellipse, Line, Triangle, Arrow, Diamond }
