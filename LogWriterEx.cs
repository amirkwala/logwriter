using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Net.Mail;
using System.Threading;

namespace Acusis.AcuSuite.Common {

	internal sealed class PathResolver {
		internal static string ResolvePath(string virtualPath) {
			virtualPath = virtualPath.Replace("~/", "").Replace('/', '\\');

			if (!Path.IsPathRooted(virtualPath)) {
				return Path.Combine(
					AppDomain.CurrentDomain.BaseDirectory,
					virtualPath);
			} else {
				return virtualPath;
			}
		}
	}

	[CLSCompliant(true)]
	public class LogWriter {
		class LogFileName {
			public string _directory, _fileName, _extension;
			int _logNumber = 0;
			string _logPath;
			internal LogFileName(string fileName) {
				_directory = Path.GetDirectoryName(fileName);
				_extension = Path.GetExtension(fileName);
				string fName = Path.GetFileNameWithoutExtension(fileName);
				int n = fName.LastIndexOf('_');
				if (n == -1) {
					_fileName = fName;
				} else {
					_fileName = fName.Substring(0, n);
					string temp = fName.Substring(n + 1);
					int.TryParse(temp, out _logNumber);
				}
                _logPath = Path.Combine(_directory, _fileName + "_" + _logNumber.ToString() + _extension);
			}

			internal string LogFilePath { get { return _logPath; } }
			internal string AdvanceToNextLog(int maxCount) {
				_logNumber++;
				bool deleteExisting = false;
				if (_logNumber >= maxCount) { _logNumber = 0; deleteExisting = true; }
				_logPath = Path.Combine(_directory, _fileName + "_" + _logNumber.ToString() + _extension);
				if (deleteExisting) { File.Delete(_logPath); }
				return LogFilePath;
			}
		}
		private static Dictionary<string, LogFileName> _logs = new Dictionary<string, LogFileName>();

		private string logKey = string.Empty;

		private static NameValueCollection m_oNameValueList = null;
		private static string _UId = string.Empty;
		private static bool logEnabled = true;
		private static long logSize = 1048576;
		private static long minPriority = 0;
		private static long maxPriority = 0;
		private static long maxLogFiles = 10;
		private static object sync = new object();
		private static string initialLog = string.Empty;

        private static MailerDetails oMailerDetails = null;

		public string FilePath {
			get {
				if (string.IsNullOrEmpty(logKey)) {
					logKey = initialLog;
				}

				if (_logs.ContainsKey(logKey) == false) {
					_logs.Add(logKey, new LogFileName(logKey));
				}

				var logFile = _logs[logKey];
				FileInfo info = new FileInfo(logFile.LogFilePath);
				while (info.Exists && (info.Length / 1024) > logSize) {

					info = new FileInfo(logFile.AdvanceToNextLog((int)maxLogFiles));
				}
				return logFile.LogFilePath;
			}
			set {
				this.logKey = value;
				if (_logs.ContainsKey(value) == false) {
					_logs.Add(value, new LogFileName(value));
				}
			}
		}

		static LogWriter() {
			Initialize();
		}


		public LogWriter() { }


		private static void Initialize() {
			m_oNameValueList = (NameValueCollection)ConfigurationManager.GetSection("LoggingConfiguration");
			if (m_oNameValueList == null || m_oNameValueList["EnableLog"] == null || m_oNameValueList["EnableLog"] == "0") {
				LogWriter.logEnabled = false;
				return;
			}
			LogWriter.initialLog = PathResolver.ResolvePath(m_oNameValueList["LogPath"]);
			Backup();
			Directory.CreateDirectory(Path.GetDirectoryName(LogWriter.initialLog));
			long num = ReadNumericValue("LogSizeInKB");
			if (num != (long)0) {
				LogWriter.logSize = num;
			}
			num = ReadNumericValue("MaxLogFiles");
			if (num != (long)0) {
				LogWriter.maxLogFiles = num;
			}
			LogWriter.minPriority = ReadNumericValue("MinPriority");
			LogWriter.maxPriority = ReadNumericValue("MaxPriority");

            num = ReadNumericValue("AlertLevel");
            if (num > (long)0)
            {
                oMailerDetails = new MailerDetails();

                oMailerDetails.AlertLevel = ReadNumericValue("AlertLevel");
                oMailerDetails.SMTPPort = ReadNumericValue("MailPort");
                oMailerDetails.SMTPServer = m_oNameValueList["MailServer"];
                oMailerDetails.Receipients = m_oNameValueList["Recipients"];
                oMailerDetails.Sender = m_oNameValueList["Sender"];
            }
            else
            {
                oMailerDetails = new MailerDetails();
            }

		}

		private static void Backup() {
			try {
				string directoryName = Path.GetDirectoryName(initialLog);
				if (Directory.Exists(directoryName)) {
					DirectoryInfo directoryInfo = new DirectoryInfo(directoryName);
					string[] name = new string[] { directoryInfo.Name, "_", 
						DateTime.Now.ToString("dd_MMM_yyyy"), "_", DateTime.Now.Ticks.ToString() };
					string backupPath = string.Concat(name);
					Directory.Move(directoryName, string.Concat(Path.GetDirectoryName(directoryName), "\\", backupPath));
				}
			} catch (Exception){}
		}

		private void WriteLogData(StreamWriter log, string strUniqueKey, params string[] strDataArray) {
			log.Write(DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss.fff tt"));
			log.Write("\t");
			log.Write(strUniqueKey);
			log.Write("\t");
			int lastItem = strDataArray.Length - 1;
			for (int i = 0; i < (int)strDataArray.Length; i++) {
				log.Write(strDataArray[i]);
				if (i < lastItem) { log.Write("\r\n\t\t\t\t\t"); }
			}
            log.WriteLine();
		}

		private bool LogRequired(long nPriority) {
			if (!LogWriter.logEnabled || string.IsNullOrEmpty(FilePath)) {
				return false;
			}
			if (nPriority >= LogWriter.minPriority && nPriority <= LogWriter.maxPriority) {
				return true;
			}
			return nPriority == (long)-1;
		}

		private static long ReadNumericValue(string strKey) {
			long num = (long)0;
			string item = m_oNameValueList[strKey];
			if (!string.IsNullOrEmpty(item)) {
				num = long.Parse(item);
			}
			return num;
		}

		private void WriteLog(int priority, string content, params object[] args) {
			if (minPriority <= priority) {
				WriteLog(string.Format(content, args), _UId, priority);
			}
		}

		public void WriteDebugLog(string content, params object[] args) {
			WriteLog(1, content, args);
		}

		public void WriteInfoLog(string content, params object[] args) {
			WriteLog(2, content, args);
		}

		public void WriteWarningLog(string content, params object[] args) {
			WriteLog(3, content, args);
		}

		public void WriteErrorLog(string content, params object[] args) 
        {
			WriteLog(4, content, args);

		}

		public void SetUniqueData(string strData) {
			LogWriter._UId = strData;
		}

        public bool WriteException(Exception e)
        {
            try
            {
                using (var logStream = new StreamWriter(FilePath, true))
                {
                    var currentExp = e;
                    while (currentExp != null)
                    {
                        WriteLogData(logStream, currentExp.Message, currentExp.StackTrace);
                        SendAlerts(currentExp.Message, currentExp.StackTrace, ((FileStream)(logStream.BaseStream)).Name);
                        currentExp = currentExp.InnerException;
                    }
                }
            }
            catch (Exception)
            {
            }
            return true;
        }

        private void SendAlerts(string ErrorMessage, string StackTrace, string LogPath)
        {
            if (oMailerDetails.AlertLevel > 0)
            {
                AlertDetails oAlertDetails = new AlertDetails();
                Process oProcess = Process.GetCurrentProcess();
                oAlertDetails.LoggerName = oProcess.ProcessName;
                oAlertDetails.ErrorDetails = ErrorMessage;
                oAlertDetails.StackDetails = StackTrace;
                oAlertDetails.EventTime = DateTime.Now;
                oAlertDetails.MailerDetails = oMailerDetails;
                oAlertDetails.LogPath = LogPath;

                Thread thread = new Thread(() => SendAlerts(oAlertDetails));
                thread.Start();
            }
        }

        private void SendAlerts(AlertDetails oAlertDetails)
        {
            AddAlertstoEventVwr(oAlertDetails);
            SendMailforAlerts(oAlertDetails);
        }
        private void AddAlertstoEventVwr(AlertDetails oAlertDetails)
        {
            try
            {
                string sSource = string.Format("Acusis Logger {0}", oAlertDetails.LoggerName);
                string sLog = "Application";
                string sEvent = string.Format("Exception : \r\n\r\nMessage: {0} \r\n\r\nStack Trace: {1} \r\n\r\nLogPath: {2) \r\n\r\nTime: {3}", oAlertDetails.ErrorDetails, oAlertDetails.StackDetails, oAlertDetails.LogPath, oAlertDetails.EventTime.ToString());

                if (!EventLog.SourceExists(sSource))
                {
                    EventLog.CreateEventSource(sSource, sLog);
                }

                EventLog.WriteEntry(sSource, sEvent, EventLogEntryType.Error, 1000, 1);

            }
            catch (Exception)
            {
            }
        }

        private void SendMailforAlerts(AlertDetails oAlertDetails)
        {

            try
            {
                MailMessage oMessage = new MailMessage();
                string[] oSenderList = oAlertDetails.MailerDetails.Receipients.Split(';');
                foreach (string oToEmail in oSenderList)
                {
                    if(!string.IsNullOrEmpty(oToEmail))
                        oMessage.To.Add(oToEmail);
                }

                if(oAlertDetails.MailerDetails.Sender == null)
                    oMessage.From = new MailAddress("noreply@acusis.com");
                else
                    oMessage.From = new MailAddress(oAlertDetails.MailerDetails.Sender);

                oMessage.Subject = string.Format("Exception Alert from {0}", oAlertDetails.LoggerName);
                oMessage.IsBodyHtml = false;
                string strBody = string.Format("<p>Hi Team,</p>"
                                    + "<p>Log Exception from <strong>{0}</strong>.</p>"
                                    + "<p><strong>Error:</strong></p>"
                                    + "<p>{1}</p>"
                                    + "<p><strong>Stack Trace:</strong></p>"
                                    + "<p>{2}</p>"
                                    + "<p><strong>System Name:</strong></p>"
                                    + "<p>{3}</p>"
                                    + "<p><strong>Log Path:</strong></p>"
                                    + "<p>{4}</p>"
                                    + "<p><strong>Time: </strong>{5}</p>"
                                    + "<p>Thanks,</p>"
                                    + "<p>Log Writer</p>", oAlertDetails.LoggerName, oAlertDetails.ErrorDetails, oAlertDetails.StackDetails, System.Environment.MachineName, oAlertDetails.LogPath, oAlertDetails.EventTime.ToString());
                oMessage.Body = strBody;
                AlternateView HtmlView = AlternateView.CreateAlternateViewFromString(strBody, null, "text/html");
                oMessage.AlternateViews.Add(HtmlView);

                SmtpClient oClient = new SmtpClient(oAlertDetails.MailerDetails.SMTPServer);
                if (oAlertDetails.MailerDetails.SMTPPort > 0)
                    oClient.Port = (int)oAlertDetails.MailerDetails.SMTPPort;

                oClient.Send(oMessage);
            }
            catch (Exception)
            {
            }
        }

		public bool WriteLog(string strData, long nPriority) {
			return this.WriteLog(strData, LogWriter._UId, nPriority);
		}

		public bool WriteLog(string strData, string strUniqueString, long nPriority) {
			try {
				if (this.LogRequired(nPriority)) {
					using (StreamWriter logStream = new StreamWriter(FilePath, true)) {
						WriteLogData(logStream, strUniqueString, strData);
					}
				}
			} catch (Exception) { }
			return true;
		}
	}

    public class MailerDetails
    {
        private long nAlertLevel = 0;
        private long nSMTPPort = -1;

        private string strReceipients = string.Empty;
        private string strSender = string.Empty;
        private string strSMTPServer = string.Empty;

        public string Receipients
        {
            get { return strReceipients; }
            set { strReceipients = value; }
        }
        public string SMTPServer
        {
            get { return strSMTPServer; }
            set { strSMTPServer = value; }
        }
        public long AlertLevel
        {
            get { return nAlertLevel; }
            set { nAlertLevel = value; }
        }
        public long SMTPPort
        {
            get { return nSMTPPort; }
            set { nSMTPPort = value; }
        }
        public string Sender
        {
            get { return strSender; }
            set { strSender = value; }
        }

    }

    public class AlertDetails
    {

        #region Data Members

        private string m_strLoggerName = string.Empty;
        private string m_strLogPath= string.Empty;
        private string m_strErrorDetails = string.Empty;
        private DateTime m_EventTime = DateTime.MinValue;
        private string m_strStackDetails = string.Empty;
        private MailerDetails oMailerDetails = null;

        #endregion


        public string LoggerName
        {
            get { return m_strLoggerName; }
            set { m_strLoggerName = value; }
        }

        public string ErrorDetails
        {
            get { return m_strErrorDetails; }
            set { m_strErrorDetails = value; }
        }

        public string StackDetails
        {
            get { return m_strStackDetails; }
            set { m_strStackDetails = value; }
        }

        public DateTime EventTime
        {
            get { return m_EventTime; }
            set { m_EventTime = value; }
        }

        public MailerDetails MailerDetails
        {
            get { return oMailerDetails; }
            set { oMailerDetails = value; }
        }
        public string LogPath
        {
            get { return m_strLogPath; }
            set { m_strLogPath = value; }
        }
        
    }
}