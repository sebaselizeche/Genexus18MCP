using System;
using System.Collections.Generic;

namespace GxMcp.Worker.Services
{
    public class BatchService
    {
        private readonly KbService _kbService;
        private readonly WriteService _writeService;
        private readonly List<BatchItem> _buffer = new List<BatchItem>();

        public BatchService(KbService kbService, WriteService writeService)
        {
            _kbService = kbService;
            _writeService = writeService;
        }

        public string ProcessBatch(string action, string name, string code)
        {
            if (action == "Add")
            {
                _buffer.Add(new BatchItem { Name = name, Code = code });
                return "{\"status\":\"Buffered\"}";
            }
            else if (action == "Commit")
            {
                int count = 0;
                foreach (var item in _buffer)
                {
                    _writeService.WriteObject(item.Name, "Source", item.Code);
                    count++;
                }
                _buffer.Clear();
                return "{\"status\":\"Committed\", \"count\":" + count + "}";
            }
            return "{\"error\":\"Unknown action\"}";
        }

        private class BatchItem { public string Name; public string Code; }
    }
}
