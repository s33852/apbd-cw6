using Microsoft.AspNetCore.Mvc;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppointmentsController : ControllerBase
    {
        private readonly string _connectionString;

        public AppointmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Missing 'DefaultConnection' in appsettings.json.");
        }
    }
}
