using System;

namespace PipetteTool
{
    // hot swap attribute for hot-swapping debug
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class HotSwappableAttribute : Attribute
    {
    }
}