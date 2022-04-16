using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace ProvisionOpenEdXPlatform
{
    public class Utils
    {

        public static string GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }

		public static void Email(string htmlString, ILogger log, MailMessage message, string subject, string attachmentPath = "")
		{
			string SendGridKey = GetEnvironmentVariable("SendGridKey");
			string SendGridName = GetEnvironmentVariable("SendGridName");
			string FROM = GetEnvironmentVariable("SendGridName");
			try
			{
				SmtpClient smtp = new SmtpClient();
				message.Subject = $"{subject}";
				if (!attachmentPath.Equals(string.Empty))
				{
					message.Attachments.Add(new Attachment(attachmentPath));
				}
				message.IsBodyHtml = true;
				message.Body = htmlString;
				message.From = new MailAddress(FROM);
				smtp.Port = 587;
				smtp.Host = "smtp.sendgrid.net";
				smtp.EnableSsl = true;
				smtp.UseDefaultCredentials = false;
				smtp.Credentials = new NetworkCredential(SendGridName, SendGridKey);
				smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
				smtp.Send(message);
			}
			catch (Exception e)
			{
				log.LogInformation(e.Message);
			}
		}

		public static string DateAndTime()
        {
            return $"{ DateTime.Now.ToShortDateString()} { DateTime.Now.ToShortTimeString()}";
        }

    }
}
