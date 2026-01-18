using Microsoft.AspNetCore.Mvc;

namespace ZenithFin.Api.v1
{
    public class Routing : ControllerBase
    {
        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(new[] { "Alice", "Bob" });
        }
    }
}
