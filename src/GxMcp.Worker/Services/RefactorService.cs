using System;

namespace GxMcp.Worker.Services
{
    public class RefactorService
    {
        private readonly KbService _kbService;

        public RefactorService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string Refactor(string name, string action)
        {
            return "{\"status\":\"Refactor not implemented yet\"}";
        }
    }
}
