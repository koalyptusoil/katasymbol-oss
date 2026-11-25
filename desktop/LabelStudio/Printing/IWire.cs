namespace LabelStudio;

public interface IWire : IDisposable
{
    byte[] Send(byte code, int param = 0, int count = 0);
    PrinterStatus Status();
    void Upload(byte[] stream, int speed, bool modern);
}
