using System;

namespace GxMcp.Worker.Services
{
    public class ForgeService
    {
        private readonly KbService _kbService;
        private readonly WriteService _writeService;

        public ForgeService(KbService kbService, WriteService writeService)
        {
            _kbService = kbService;
            _writeService = writeService;
        }
        public string CreateObject(string target, string payload)
        {
            return "{\"status\":\"CreateObject not implemented yet\"}";
        }
    }
}
