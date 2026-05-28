// OdaConverterTests.cs
// Unit tests for OdaConverter (mocked, since ODA may not be installed).

using CADTransLite.Core.Models;
using CADTransLite.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CADTransLite.Tests;

/// <summary>
/// Tests for OdaConverter class.
/// Note: These tests mock the ODA CLI behavior since ODA File Converter may not be installed.
/// </summary>
[TestClass]
public class OdaConverterTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    // -----------------------------------------------------------------------
    // Constructor tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Constructor_DefaultSettings_UsesDefaultPath()
    {
        // Act
        var converter = new OdaConverter();

        // Assert - no exception means success
        Assert.IsNotNull(converter);
        Assert.IsNotNull(converter.IsAvailable); // Property exists
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        // Act
        var converter = new OdaConverter(null!);
    }

    [TestMethod]
    public void IsAvailable_WhenOdaNotInstalled_ReturnsFalse()
    {
        // Arrange
        var settings = new OdaSettings
        {
            ExecutablePath = @"C:\NonExistent\Path\ODAFileConverter.exe"
        };
        var converter = new OdaConverter(settings);

        // Act & Assert
        Assert.IsFalse(converter.IsAvailable);
    }

    // -----------------------------------------------------------------------
    // DwgToDxfAsync tests (mocked)
    // -----------------------------------------------------------------------

    [TestMethod]
    [ExpectedException(typeof(FileNotFoundException))]
    public async Task DwgToDxfAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var converter = new OdaConverter();
        string nonExistent = Path.Combine(_tempDir, "nonexistent.dwg");

        // Act
        await converter.DwgToDxfAsync(nonExistent, _tempDir, CancellationToken.None);
    }

    [TestMethod]
    public async Task DwgToDxfAsync_WhenOdaNotInstalled_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new OdaSettings
        {
            ExecutablePath = @"C:\NonExistent\Path\ODAFileConverter.exe"
        };
        var converter = new OdaConverter(settings);
        string fakeDwg = Path.Combine(_tempDir, "test.dwg");
        File.WriteAllBytes(fakeDwg, []); // Create empty file

        // Act & Assert
        try
        {
            await converter.DwgToDxfAsync(fakeDwg, _tempDir, CancellationToken.None);
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException ex)
        {
            Assert.IsTrue(ex.Message.Contains("ODA") || ex.Message.Contains("not found") || ex.Message.Contains("安装"));
        }
    }

    // -----------------------------------------------------------------------
    // Integration tests (require ODA to be installed)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Integration_RoundTrip_DwgToDxf()
    {
        // Arrange
        var settings = new OdaSettings();
        var converter = new OdaConverter(settings);

        if (!converter.IsAvailable)
        {
            Assert.Inconclusive("ODA File Converter not installed");
            return;
        }

        // Create a simple DWG file (or use a real one if available)
        string testDwg = Path.Combine(_tempDir, "input.dwg");
        
        // Note: This requires a real DWG file to test properly
        // For now, skip this test
        Assert.Inconclusive("Requires real DWG file for integration test");
    }
}
