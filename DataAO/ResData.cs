namespace Matory.DataAO
{
    public class ResData
    {
        public int Code { get; set; }
        public bool Msg { get; set; }
        public string Data { get; set; }
        public ResData(int code, bool msg, string data)
        {
            this.Code = code;
            this.Msg = msg;
            this.Data = data;
        }
    }
}
