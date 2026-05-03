using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace APBD_TASK6.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing 'DefaultConnection' in appsettings.json.");
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var results = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            (object?)status ?? DBNull.Value;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            (object?)patientLastName ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            });
        }

        return Ok(results);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email     AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber,
                s.Name AS Specialization
            FROM dbo.Appointments a
            JOIN dbo.Patients       p ON p.IdPatient       = a.IdPatient
            JOIN dbo.Doctors        d ON d.IdDoctor        = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound(new ErrorResponseDto { Message = $"Appointment {idAppointment} not found." });

        var dto = new AppointmentDetailsDto
        {
            IdAppointment      = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate    = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status             = reader.GetString(reader.GetOrdinal("Status")),
            Reason             = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes      = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                                     ? null
                                     : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt          = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            PatientFullName    = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail       = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
            DoctorFullName     = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("LicenseNumber")),
            Specialization     = reader.GetString(reader.GetOrdinal("Specialization")),
        };

        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new ErrorResponseDto { Message = "Reason is required." });

        if (dto.Reason.Length > 250)
            return BadRequest(new ErrorResponseDto { Message = "Reason must be at most 250 characters." });

        if (dto.AppointmentDate <= DateTime.UtcNow)
            return BadRequest(new ErrorResponseDto { Message = "Appointment date cannot be in the past." });

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var cmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;", connection))
        {
            cmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return NotFound(new ErrorResponseDto { Message = $"Patient {dto.IdPatient} not found." });
            if ((bool)result == false)
                return BadRequest(new ErrorResponseDto { Message = $"Patient {dto.IdPatient} is not active." });
        }

        await using (var cmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;", connection))
        {
            cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return NotFound(new ErrorResponseDto { Message = $"Doctor {dto.IdDoctor} not found." });
            if ((bool)result == false)
                return BadRequest(new ErrorResponseDto { Message = $"Doctor {dto.IdDoctor} is not active." });
        }

        await using (var cmd = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled';
            """, connection))
        {
            cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
            var count = (int)(await cmd.ExecuteScalarAsync())!;
            if (count > 0)
                return Conflict(new ErrorResponseDto
                {
                    Message = "Doctor already has a scheduled appointment at that time."
                });
        }

        await using (var cmd = new SqlCommand("""
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);
            """, connection))
        {
            cmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
            cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
            cmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;

            var newId = (int)(await cmd.ExecuteScalarAsync())!;
            return CreatedAtAction(nameof(GetAppointment), new { idAppointment = newId }, new { idAppointment = newId });
        }
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto dto)
    {
        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(dto.Status))
            return BadRequest(new ErrorResponseDto { Message = "Status must be Scheduled, Completed, or Cancelled." });

        if (string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new ErrorResponseDto { Message = "Reason is required." });

        if (dto.Reason.Length > 250)
            return BadRequest(new ErrorResponseDto { Message = "Reason must be at most 250 characters." });

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        DateTime currentDate;
        string currentStatus;
        await using (var cmd = new SqlCommand(
            "SELECT AppointmentDate, Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;",
            connection))
        {
            cmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return NotFound(new ErrorResponseDto { Message = $"Appointment {idAppointment} not found." });
            currentDate   = reader.GetDateTime(reader.GetOrdinal("AppointmentDate"));
            currentStatus = reader.GetString(reader.GetOrdinal("Status"));
        }

        bool dateChanging = dto.AppointmentDate != currentDate;
        if (currentStatus == "Completed" && dateChanging)
            return Conflict(new ErrorResponseDto { Message = "Cannot change the date of a completed appointment." });

        await using (var cmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;", connection))
        {
            cmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return NotFound(new ErrorResponseDto { Message = $"Patient {dto.IdPatient} not found." });
            if ((bool)result == false)
                return BadRequest(new ErrorResponseDto { Message = $"Patient {dto.IdPatient} is not active." });
        }

        await using (var cmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;", connection))
        {
            cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return NotFound(new ErrorResponseDto { Message = $"Doctor {dto.IdDoctor} not found." });
            if ((bool)result == false)
                return BadRequest(new ErrorResponseDto { Message = $"Doctor {dto.IdDoctor} is not active." });
        }

        if (dateChanging)
        {
            await using var cmd = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                  AND AppointmentDate = @AppointmentDate
                  AND Status = N'Scheduled'
                  AND IdAppointment <> @IdAppointment;
                """, connection);
            cmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
            cmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
            var count = (int)(await cmd.ExecuteScalarAsync())!;
            if (count > 0)
                return Conflict(new ErrorResponseDto
                {
                    Message = "Doctor already has a scheduled appointment at that time."
                });
        }

        await using (var cmd = new SqlCommand("""
            UPDATE dbo.Appointments
            SET IdPatient       = @IdPatient,
                IdDoctor        = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status          = @Status,
                Reason          = @Reason,
                InternalNotes   = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """, connection))
        {
            cmd.Parameters.Add("@IdPatient",        SqlDbType.Int).Value           = dto.IdPatient;
            cmd.Parameters.Add("@IdDoctor",          SqlDbType.Int).Value           = dto.IdDoctor;
            cmd.Parameters.Add("@AppointmentDate",   SqlDbType.DateTime2).Value     = dto.AppointmentDate;
            cmd.Parameters.Add("@Status",            SqlDbType.NVarChar, 30).Value  = dto.Status;
            cmd.Parameters.Add("@Reason",            SqlDbType.NVarChar, 250).Value = dto.Reason;
            cmd.Parameters.Add("@InternalNotes",     SqlDbType.NVarChar, 500).Value =
                (object?)dto.InternalNotes ?? DBNull.Value;
            cmd.Parameters.Add("@IdAppointment",     SqlDbType.Int).Value           = idAppointment;

            await cmd.ExecuteNonQueryAsync();
        }

        return Ok();
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string currentStatus;
        await using (var cmd = new SqlCommand(
            "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection))
        {
            cmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
            var result = await cmd.ExecuteScalarAsync();
            if (result is null)
                return NotFound(new ErrorResponseDto { Message = $"Appointment {idAppointment} not found." });
            currentStatus = (string)result;
        }

        if (currentStatus == "Completed")
            return Conflict(new ErrorResponseDto { Message = "Cannot delete a completed appointment." });

        await using (var cmd = new SqlCommand(
            "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection))
        {
            cmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
            await cmd.ExecuteNonQueryAsync();
        }

        return NoContent();
    }
}
