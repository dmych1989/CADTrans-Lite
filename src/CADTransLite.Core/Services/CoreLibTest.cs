// Quick test to verify the new ErrorLogger is included
using CADTransLite.Core.Services;

namespace CADTransLite.Core.Services;

// This class should include the new ErrorLogger implementation
public class CoreLibTest
{
    public static bool HasErrorLogger()
    {
        try
        {
            var logger = ErrorLogger.Instance;
            return true;
        }
        catch
        {
            return false;
        }
    }
}