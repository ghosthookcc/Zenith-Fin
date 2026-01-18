namespace ZenithFin.Api
{
    public class ApiManager
    {
        private WebApplication? _app;
        public void Start()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            _app = builder.Build();

            if (_app.Environment.IsDevelopment())
            {
                _app.UseSwagger();
                _app.UseSwaggerUI();
            }
            _app.UseHttpsRedirection();
            _app.UseAuthorization();

            _app.MapControllers();

            _app.Run();
        }
    }
}
