using GenICam.Net.GenApi;

namespace GenICam.Net.Tests;

[TestFixture]
public class RealCameraXmlTests
{
    // Real camera description XMLs committed under the test project's CameraXml folder.
    // Add a camera by dropping its XML in that folder and adding a case here:
    //   { fileName, vendorName, modelName, schemaVersion, exactNodeCount }
    private static readonly object[] CameraXmlCases =
    [
        new object[]
        {
            "TeledyneDalsa_GenieNano.xml",
            "TeledyneDALSA",
            "Nano",
            new Version(1, 1, 0),
            1348
        },
    ];

    [TestCaseSource(nameof(CameraXmlCases))]
    public void Parse_SuppliedCameraXml_LoadsExpectedCoreFeatures(
        string fileName,
        string expectedVendor,
        string expectedModel,
        Version expectedSchemaVersion,
        int expectedNodeCount)
    {
        var filePath = FindCameraXml(fileName);
        Assert.That(filePath, Is.Not.Null,
            $"Camera XML test data '{fileName}' was not found. It should live under the test project's CameraXml folder and be copied to the output directory.");

        var nodeMap = NodeMapParser.ParseFile(filePath!);

        Assert.That(nodeMap.VendorName, Is.EqualTo(expectedVendor));
        Assert.That(nodeMap.ModelName, Is.EqualTo(expectedModel));
        Assert.That(nodeMap.SchemaVersion, Is.EqualTo(expectedSchemaVersion));
        Assert.That(nodeMap.Nodes.Count, Is.EqualTo(expectedNodeCount));

        // Core SFNC features that every GigE Vision camera exposes, resolved by name
        // through the (group-flattened) node map. These guard against a regression in
        // the parser that would silently drop feature nodes.
        Assert.That(nodeMap.GetNode("Device"), Is.InstanceOf<IRegister>());
        Assert.That(nodeMap.GetNode("Width"), Is.InstanceOf<IInteger>());
        Assert.That(nodeMap.GetNode("Height"), Is.InstanceOf<IInteger>());
        Assert.That(nodeMap.GetNode("PixelFormat"), Is.InstanceOf<IEnumeration>());
        Assert.That(nodeMap.GetNode("AcquisitionStart"), Is.InstanceOf<ICommand>());
    }

    private static string? FindCameraXml(string fileName)
    {
        var candidate = Path.Combine(TestContext.CurrentContext.TestDirectory, "CameraXml", fileName);
        return File.Exists(candidate) ? candidate : null;
    }
}
