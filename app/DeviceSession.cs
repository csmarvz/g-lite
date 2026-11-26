using System;
using System.Threading.Tasks;

// A device's connection and its work queue, without UI. Work runs one at a time off the UI thread, in the order it was queued, on a
// connection opened when it's first needed (opened runs then: for what the device forgets when it's unplugged) and dropped when a
// call fails with anything but the device's refusal (lost runs then; the next work opens it again). DevicePage passes its own
// callbacks and moves their results to the UI thread.
class DeviceSession
{
    // The device as the last scan found it: its path is where the next connection opens.
    public FoundDevice Found;
    readonly Hidpp fake;
    readonly Action<Hidpp> opened;
    readonly Action lost;
    Hidpp connection;
    Task queue = Task.FromResult(0);
    // Whether the work running now opened the connection, so opened has just run.
    bool openedNow;
    // Set when a call failed and the connection was dropped, until one is opened again.
    volatile bool isLost;
    bool queued;

    public DeviceSession(FoundDevice found, Hidpp fake, Action<Hidpp> opened, Action lost)
    {
        Found = found;
        this.fake = fake;
        this.opened = opened;
        this.lost = lost;
    }

    public Task Queue { get { return queue; } }

    public bool Demo { get { return fake != null; } }

    public bool OpenedNow { get { return openedNow; } }

    public bool IsLost { get { return isLost; } }

    // Whether any work was queued yet.
    public bool Queued { get { return queued; } }

    public string Label { get { return Found.ShortName == "" ? Found.Name : Found.ShortName; } }

    // done runs after work, failed with what work threw; both on the queue, as work does.
    public void Run(Action<Hidpp> work, Action done, Action<Exception> failed)
    {
        queued = true;
        queue = queue.ContinueWith(_ =>
        {
            try
            {
                openedNow = connection == null;
                if (connection == null)
                {
                    connection = fake ?? new Hidpp(Found.Path, Found.Index, Found.ShortPath);
                    connection.DeviceLabel = Label;
                    isLost = false;
                    opened(connection);
                }
                work(connection);
                done();
            }
            catch (Exception ex)
            {
                Log.Add(Label, "error", ex.Message);
                if (connection != null && !(ex is HidppError) && !(ex is RefusedException))
                {
                    connection.Dispose();
                    connection = null;
                    isLost = true;
                    lost();
                }
                failed(ex);
            }
        });
    }

    // After the work queued before.
    public void Close() { queue = queue.ContinueWith(_ => { if (connection != null) connection.Dispose(); connection = null; }); }
}
