using netDxf;
using netDxf.Entities;

var doc = new DxfDocument();

// Add TEXT entities
doc.Entities.Add(new Text("Hello World") { Position = new netDxf.Vector3(10, 50, 0), Height = 5 });
doc.Entities.Add(new Text("CAD Drawing") { Position = new netDxf.Vector3(10, 40, 0), Height = 5 });
doc.Entities.Add(new Text("Section A-A") { Position = new netDxf.Vector3(10, 30, 0), Height = 5 });

// Add MTEXT entities
doc.Entities.Add(new MText("This is a multi-line note.\nSecond line of text.") { Position = new netDxf.Vector3(10, 20, 0), Height = 3 });
doc.Entities.Add(new MText("Material: Steel\nGrade: Q235") { Position = new netDxf.Vector3(10, 10, 0), Height = 3 });

string outputPath = @"E:\CADTrans Lite\test_sample.dxf";
doc.Save(outputPath);
Console.WriteLine($"DXF saved to: {outputPath}");
