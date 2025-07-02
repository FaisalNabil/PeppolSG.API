using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PeppolSG.API.Models;

namespace PeppolSG.API.Service.Interfaces
{
    public interface IMimeParserService
    {
        Task<List<MimePart>> ParseMultipartRequest(Stream stream, string contentType);
    }
} 