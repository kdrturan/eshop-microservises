using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildingBlocks.Exceptions
{
    public class BadRequestException : Exception
    {
        public BadRequestException() : base("Bad request exception.")
        {
        }
        public BadRequestException(string message) : base(message)
        {
        }
        public BadRequestException(string message, string details) : base(message)
        {
            Details = details;
        }

        public string? Details { get; set; }
    }
}
