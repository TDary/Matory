using System.Net.Sockets;

namespace Matory.DataAO
{
    public class Session
    {
		private Socket _sockeclient;
		private byte[] _buffer;
		private string _ip;
		private bool _isweb = false;

		public Socket SockeClient
		{
			set { _sockeclient = value; }
			get { return _sockeclient; }
		}

		public byte[] buffer
		{
			set { _buffer = value; }
			get { return _buffer; }
		}

		public string IP
		{
			set { _ip = value; }
			get { return _ip; }
		}

		public bool isWeb
		{
			set { _isweb = value; }
			get { return _isweb; }
		}
	}
}
