using System.Text;
using Artech.Architecture.Common.Objects;

namespace GxMcp.Worker.Parsers
{
    public interface IDslParser
    {
        void Serialize(KBObject obj, StringBuilder sb);
        void Parse(KBObject obj, string text);
    }
}
