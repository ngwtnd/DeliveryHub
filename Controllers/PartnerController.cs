using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using DeliveryHubWeb.Models;

namespace DeliveryHubWeb.Controllers
{
    // Renamed to avoid duplicate class definition
    public class PartnerControllerHelper : Controller
    {
        private readonly Data.ApplicationDbContext _context;

        public PartnerControllerHelper(Data.ApplicationDbContext context)
        {
            _context = context;
        }
    }
}
