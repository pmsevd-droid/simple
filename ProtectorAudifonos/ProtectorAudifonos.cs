// Protector de audífonos
// Silencia los altavoces de la laptop cuando los audífonos se desconectan,
// muestra una alerta y deja un ícono junto al reloj.
//
// Funciona con la API de audio de Windows (Core Audio): Windows avisa al
// programa cada vez que cambia el dispositivo de salida, sin revisar a cada rato.
//
// El estilo (avisos, menú, íconos, "Acerca de") viene de ..\comun\Comun.cs,
// compartido con las demás utilidades de la colección.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Comun;

[assembly: AssemblyTitle("Protector de audífonos")]
[assembly: AssemblyDescription("Silencia los altavoces cuando se desconectan los audífonos, para que no suene nada fuerte.")]
[assembly: AssemblyProduct("Protector de audífonos")]
[assembly: AssemblyCompany("Pierol Solano")]
[assembly: AssemblyCopyright("Gratis y de código abierto")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace ProtectorAudifonos
{
    // =====================================================================
    //  Programa principal
    // =====================================================================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\ProtectorAudifonos_v1", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Protector de audífonos ya está abierto.\nBúscalo junto al reloj (o en la flechita ^ de la barra de tareas).",
                        "Protector de audífonos", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Estilo.Preparar();
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }

    // =====================================================================
    //  Configuración (se guarda en %APPDATA%\ProtectorAudifonos\config.ini)
    // =====================================================================
    class Settings
    {
        public bool Enabled = true;        // protección activada
        public bool LowVolumeMode = false; // false = silenciar, true = bajar al 5 %
        public bool Alert = true;          // mostrar alerta
        public bool PreMute = true;        // silenciar los altavoces por adelantado
        public bool Startup = true;        // iniciar con Windows
        public bool FirstRun = false;

        const string App = "ProtectorAudifonos";

        public static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, string> d = Ini.Leer(App);
            if (d == null) { s.FirstRun = true; return s; }
            s.Enabled = Ini.Bool(d, "enabled", s.Enabled);
            s.LowVolumeMode = Ini.Bool(d, "lowvolume", s.LowVolumeMode);
            s.Alert = Ini.Bool(d, "alert", s.Alert);
            s.PreMute = Ini.Bool(d, "premute", s.PreMute);
            s.Startup = Ini.Bool(d, "startup", s.Startup);
            return s;
        }

        public void Save()
        {
            Ini.Guardar(App, new string[] {
                "enabled=" + Ini.B(Enabled),
                "lowvolume=" + Ini.B(LowVolumeMode),
                "alert=" + Ini.B(Alert),
                "premute=" + Ini.B(PreMute),
                "startup=" + Ini.B(Startup)
            });
        }
    }

    // =====================================================================
    //  Core Audio (interfaces COM de Windows)
    // =====================================================================
    enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid g, int p) { fmtid = g; pid = p; }
    }

    [StructLayout(LayoutKind.Explicit)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptr;
        [FieldOffset(8)] public uint uintVal;
        [FieldOffset(16)] public long pad;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out int count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDB, IntPtr eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(int channel, float levelDB, IntPtr eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(int channel, float level, IntPtr eventContext);
        [PreserveSig] int GetChannelVolumeLevel(int channel, out float levelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(int channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out int step, out int stepCount);
        [PreserveSig] int VolumeStepUp(IntPtr eventContext);
        [PreserveSig] int VolumeStepDown(IntPtr eventContext);
        [PreserveSig] int QueryHardwareSupport(out int mask);
        [PreserveSig] int GetVolumeRange(out float minDB, out float maxDB, out float incrementDB);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    // Recibe los avisos de Windows (llegan en otro hilo)
    class NotificationClient : IMMNotificationClient
    {
        readonly Action onChange;
        public NotificationClient(Action onChange) { this.onChange = onChange; }
        void Fire() { try { onChange(); } catch { } }
        public void OnDeviceStateChanged(string deviceId, int newState) { Fire(); }
        public void OnDeviceAdded(string deviceId) { Fire(); }
        public void OnDeviceRemoved(string deviceId) { Fire(); }
        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId)
        {
            if (flow == EDataFlow.eRender) Fire();
        }
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    class DeviceInfo
    {
        public string Id;
        public string Name;
        public int FormFactor; // 1 = altavoces, 3 = auriculares, 5 = headset, 10 = desconocido...

        public bool IsSpeakers
        {
            get
            {
                if (FormFactor == 1) return true;
                if (FormFactor == 3 || FormFactor == 5) return false;
                string n = (Name ?? "").ToLowerInvariant();
                return FormFactor == 10 && (n.Contains("altavoc") || n.Contains("speaker") || n.Contains("bocina"));
            }
        }

        public bool IsHeadphones
        {
            get
            {
                if (FormFactor == 3 || FormFactor == 5) return true;
                string n = (Name ?? "").ToLowerInvariant();
                return n.Contains("auricular") || n.Contains("audífono") || n.Contains("audifono") ||
                       n.Contains("headphone") || n.Contains("headset") || n.Contains("cascos");
            }
        }
    }

    class AudioManager : IDisposable
    {
        const int DEVICE_STATE_ACTIVE = 1;
        const int CLSCTX_ALL = 23;

        static readonly PropertyKey PKEY_FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        static readonly PropertyKey PKEY_FormFactor = new PropertyKey(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
        static Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        IMMDeviceEnumerator en;
        NotificationClient client;

        public AudioManager(Action onChange)
        {
            en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            client = new NotificationClient(onChange);
            en.RegisterEndpointNotificationCallback(client);
        }

        public void Dispose()
        {
            try { if (en != null && client != null) en.UnregisterEndpointNotificationCallback(client); } catch { }
            Rel(en);
            en = null;
        }

        static void Rel(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { }
        }

        public DeviceInfo GetDefault()
        {
            IMMDevice d = null;
            try
            {
                if (en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out d) != 0 || d == null) return null;
                return Describe(d);
            }
            catch { return null; }
            finally { Rel(d); }
        }

        public List<DeviceInfo> GetActiveOutputs()
        {
            List<DeviceInfo> list = new List<DeviceInfo>();
            IMMDeviceCollection col = null;
            try
            {
                if (en.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out col) != 0 || col == null) return list;
                int count;
                col.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IMMDevice d = null;
                    try
                    {
                        if (col.Item(i, out d) == 0 && d != null)
                        {
                            DeviceInfo info = Describe(d);
                            if (info != null) list.Add(info);
                        }
                    }
                    finally { Rel(d); }
                }
            }
            catch { }
            finally { Rel(col); }
            return list;
        }

        public bool IsActive(string id)
        {
            if (id == null) return false;
            IMMDevice d = null;
            try
            {
                if (en.GetDevice(id, out d) != 0 || d == null) return false;
                int state;
                return d.GetState(out state) == 0 && state == DEVICE_STATE_ACTIVE;
            }
            catch { return false; }
            finally { Rel(d); }
        }

        DeviceInfo Describe(IMMDevice d)
        {
            DeviceInfo info = new DeviceInfo();
            string id;
            if (d.GetId(out id) != 0) return null;
            info.Id = id;
            info.FormFactor = 10;
            IPropertyStore ps = null;
            try
            {
                if (d.OpenPropertyStore(0, out ps) == 0 && ps != null)
                {
                    PropertyKey k = PKEY_FriendlyName;
                    PropVariant pv;
                    if (ps.GetValue(ref k, out pv) == 0)
                    {
                        if (pv.vt == 31 && pv.ptr != IntPtr.Zero) info.Name = Marshal.PtrToStringUni(pv.ptr);
                        PropVariantClear(ref pv);
                    }
                    k = PKEY_FormFactor;
                    if (ps.GetValue(ref k, out pv) == 0)
                    {
                        if (pv.vt == 19) info.FormFactor = (int)pv.uintVal;
                        PropVariantClear(ref pv);
                    }
                }
            }
            catch { }
            finally { Rel(ps); }
            if (info.Name == null) info.Name = "Dispositivo de audio";
            return info;
        }

        IAudioEndpointVolume OpenVolume(string id, out IMMDevice device)
        {
            device = null;
            if (en.GetDevice(id, out device) != 0 || device == null) return null;
            object o;
            if (device.Activate(ref IID_IAudioEndpointVolume, CLSCTX_ALL, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        public bool TryGetVolume(string id, out float volume, out bool mute)
        {
            volume = 0; mute = false;
            IMMDevice d = null; IAudioEndpointVolume v = null;
            try
            {
                v = OpenVolume(id, out d);
                if (v == null) return false;
                if (v.GetMasterVolumeLevelScalar(out volume) != 0) return false;
                if (v.GetMute(out mute) != 0) return false;
                return true;
            }
            catch { return false; }
            finally { Rel(v); Rel(d); }
        }

        public bool SetVolume(string id, float? volume, bool? mute)
        {
            IMMDevice d = null; IAudioEndpointVolume v = null;
            try
            {
                v = OpenVolume(id, out d);
                if (v == null) return false;
                if (volume.HasValue) v.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, volume.Value)), IntPtr.Zero);
                if (mute.HasValue) v.SetMute(mute.Value, IntPtr.Zero);
                return true;
            }
            catch { return false; }
            finally { Rel(v); Rel(d); }
        }
    }

    // =====================================================================
    //  Glifos de la fuente de íconos de Windows (Segoe Fluent Icons / MDL2)
    // =====================================================================
    static class Glifos
    {
        public const string Headphones = "\uE7F6";
        public const string Speaker = "\uE767";
        public const string SpeakerMuted = "\uE74F";
    }

    // =====================================================================
    //  Lógica principal + ícono de la bandeja
    // =====================================================================
    class TrayApp : ApplicationContext
    {
        class SavedState { public float Volume; public bool Mute; }

        const string RunName = "ProtectorAudifonos";
        const float LowVolume = 0.05f;

        readonly Settings s;
        readonly Control invoker;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = MenuClasico.Crear();
        readonly System.Windows.Forms.Timer pendingTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        readonly Dictionary<string, SavedState> saved = new Dictionary<string, SavedState>();
        AudioManager audio;
        DeviceInfo last;          // dispositivo de salida actual
        DeviceInfo pending;       // dispositivo anterior, cuando acabamos de pasar a altavoces
        DateTime pendingSince;
        bool cleanedUp;

        ToolStripMenuItem headerItem, enabledItem, muteModeItem, lowModeItem, alertItem, preItem, startupItem;

        public TrayApp()
        {
            s = Settings.Load();

            invoker = new Control();
            IntPtr forceHandle = invoker.Handle; // crea la ventana interna para recibir avisos
            GC.KeepAlive(forceHandle);

            BuildMenu();
            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.Icon = Iconos.Glifo(Glifos.Headphones, false);
            tray.Text = "Protector de audífonos";
            tray.Visible = true;
            MenuClasico.AbrirConClicIzquierdo(tray);

            try
            {
                audio = new AudioManager(delegate
                {
                    try { invoker.BeginInvoke((MethodInvoker)Evaluate); } catch { }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo conectar con el sistema de audio de Windows:\n" + ex.Message,
                    "Protector de audífonos", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            pendingTimer.Interval = 100;
            pendingTimer.Tick += delegate { CheckPending(); UpdateUi(); };

            pollTimer.Interval = 2000; // respaldo por si algún aviso no llega
            pollTimer.Tick += delegate { Evaluate(); };
            pollTimer.Start();

            if (audio != null)
            {
                last = audio.GetDefault();
                if (s.Enabled && s.PreMute && last != null && !last.IsSpeakers) PreProtect();
            }

            ApplyStartup();
            UpdateUi();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
            Application.ApplicationExit += delegate { Cleanup(); };

            if (s.FirstRun)
            {
                s.Save();
                Aviso.Mostrar("Ya está activo",
                    "Lo encontrarás junto al reloj. Si no lo ves, abre la flechita ^ de la barra de tareas y arrástralo afuera para tenerlo siempre visible.",
                    SystemIcons.Information);
            }
        }

        // ---------------- Menú ----------------
        void BuildMenu()
        {
            headerItem = new ToolStripMenuItem("Salida: ...");
            headerItem.Enabled = false;

            enabledItem = new ToolStripMenuItem("Proteger los altavoces");
            enabledItem.Click += delegate
            {
                s.Enabled = !s.Enabled;
                s.Save();
                pending = null; pendingTimer.Stop();
                ReapplyPreProtection();
                UpdateUi();
            };

            ToolStripMenuItem modeItem = new ToolStripMenuItem("Al desconectar los audífonos");
            muteModeItem = new ToolStripMenuItem("Silenciar los altavoces");
            lowModeItem = new ToolStripMenuItem("Bajar el volumen al 5 %");
            muteModeItem.Click += delegate { SetMode(false); };
            lowModeItem.Click += delegate { SetMode(true); };
            modeItem.DropDownItems.Add(muteModeItem);
            modeItem.DropDownItems.Add(lowModeItem);

            alertItem = new ToolStripMenuItem("Mostrar alerta");
            alertItem.Click += delegate { s.Alert = !s.Alert; s.Save(); UpdateUi(); };

            preItem = new ToolStripMenuItem("Silenciar por adelantado (evita cualquier ruido)");
            preItem.Click += delegate { s.PreMute = !s.PreMute; s.Save(); ReapplyPreProtection(); UpdateUi(); };

            startupItem = new ToolStripMenuItem("Iniciar con Windows");
            startupItem.Click += delegate { s.Startup = !s.Startup; s.Save(); ApplyStartup(); UpdateUi(); };

            ToolStripMenuItem unmuteItem = new ToolStripMenuItem("Activar sonido de los altavoces");
            unmuteItem.Click += delegate { UnmuteSpeakers(); UpdateUi(); };

            ToolStripMenuItem testItem = new ToolStripMenuItem("Probar la alerta");
            testItem.Click += delegate
            {
                Aviso.Mostrar("¡Se desconectaron los audífonos!", "Así se verá la alerta. (Esto es solo una prueba, no se cambió nada.)", SystemIcons.Warning);
            };

            ToolStripMenuItem soundItem = new ToolStripMenuItem("Abrir configuración de sonido");
            soundItem.Click += delegate
            {
                try { Process.Start("ms-settings:sound"); } catch { }
            };

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("Acerca de…");
            aboutItem.Click += delegate { AcercaDe.Mostrar(); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Salir");
            exitItem.Click += delegate { Cleanup(); ExitThread(); };

            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(enabledItem);
            menu.Items.Add(modeItem);
            menu.Items.Add(alertItem);
            menu.Items.Add(preItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(unmuteItem);
            menu.Items.Add(testItem);
            menu.Items.Add(soundItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(aboutItem);
            menu.Items.Add(exitItem);

            menu.Opening += delegate { Evaluate(); };
        }

        void SetMode(bool low)
        {
            if (s.LowVolumeMode == low) return;
            s.LowVolumeMode = low;
            s.Save();
            ReapplyPreProtection();
            UpdateUi();
        }

        // ---------------- Lógica ----------------

        // Se llama cada vez que Windows avisa de un cambio de audio.
        void Evaluate()
        {
            if (audio == null || cleanedUp) return;
            DeviceInfo cur = audio.GetDefault();
            string curId = cur == null ? null : cur.Id;
            string lastId = last == null ? null : last.Id;

            if (curId != lastId)
            {
                DeviceInfo prev = last;
                last = cur;

                if (s.Enabled && cur != null && cur.IsSpeakers && prev != null && !prev.IsSpeakers)
                {
                    // El sonido acaba de pasar a los altavoces: protegerlos YA,
                    // y luego ver si fue porque se desconectaron los audífonos
                    // o porque el usuario los eligió a propósito.
                    Protect(cur);
                    pending = prev;
                    pendingSince = DateTime.Now;
                    pendingTimer.Start();
                }
                else
                {
                    pending = null;
                    pendingTimer.Stop();
                    if (s.Enabled && s.PreMute && cur != null && !cur.IsSpeakers) PreProtect();
                }
            }

            CheckPending();
            UpdateUi();
        }

        void CheckPending()
        {
            if (pending == null || audio == null) return;
            if (!audio.IsActive(pending.Id))
            {
                // El dispositivo anterior ya no está: se desconectaron los audífonos.
                DeviceInfo p = pending;
                pending = null;
                pendingTimer.Stop();
                OnDisconnected(p);
            }
            else if ((DateTime.Now - pendingSince).TotalMilliseconds > 1500)
            {
                // Los audífonos siguen conectados: el cambio fue a propósito.
                pending = null;
                pendingTimer.Stop();
                if (last != null && last.IsSpeakers) Restore(last.Id);
            }
        }

        void OnDisconnected(DeviceInfo prev)
        {
            if (!s.Alert) return;
            string title = prev.IsHeadphones
                ? "¡Se desconectaron los audífonos!"
                : "¡Se desconectó " + prev.Name + "!";
            string body = s.LowVolumeMode
                ? "El volumen de los altavoces se bajó al 5 %."
                : "Los altavoces quedaron silenciados.";
            ShowAlert(title, body, true);
        }

        bool IsProtected(float vol, bool mute)
        {
            return s.LowVolumeMode ? (mute || vol <= LowVolume + 0.005f) : mute;
        }

        void Protect(DeviceInfo dev)
        {
            float vol; bool mute;
            if (!audio.TryGetVolume(dev.Id, out vol, out mute)) return;
            if (!IsProtected(vol, mute))
            {
                SavedState st = new SavedState();
                st.Volume = vol;
                st.Mute = mute;
                saved[dev.Id] = st; // recordar cómo estaba para poder devolverlo
                if (s.LowVolumeMode) audio.SetVolume(dev.Id, LowVolume, null);
                else audio.SetVolume(dev.Id, null, true);
            }
        }

        void PreProtect()
        {
            foreach (DeviceInfo d in audio.GetActiveOutputs())
                if (d.IsSpeakers) Protect(d);
        }

        void Restore(string id)
        {
            SavedState st;
            if (!saved.TryGetValue(id, out st)) return;
            saved.Remove(id);
            audio.SetVolume(id, st.Volume, st.Mute);
        }

        void RestoreAll()
        {
            foreach (string id in new List<string>(saved.Keys)) Restore(id);
        }

        // Al cambiar opciones: deshacer el silencio anticipado y volver a aplicarlo
        // (solo si ahora no estás usando los altavoces).
        void ReapplyPreProtection()
        {
            if (audio == null) return;
            if (last != null && last.IsSpeakers) return;
            RestoreAll();
            if (s.Enabled && s.PreMute && last != null) PreProtect();
        }

        void UnmuteSpeakers()
        {
            if (audio == null) return;
            foreach (DeviceInfo d in audio.GetActiveOutputs())
            {
                if (!d.IsSpeakers) continue;
                if (saved.ContainsKey(d.Id)) Restore(d.Id);
                float vol; bool mute;
                if (audio.TryGetVolume(d.Id, out vol, out mute) && mute) audio.SetVolume(d.Id, null, false);
            }
        }

        // ---------------- Interfaz ----------------
        void ShowAlert(string title, string body, bool showUnmute)
        {
            if (showUnmute)
                Aviso.Mostrar(title, body, SystemIcons.Warning, "Activar sonido de los altavoces",
                    delegate { UnmuteSpeakers(); UpdateUi(); }, null);
            else
                Aviso.Mostrar(title, body, SystemIcons.Warning);
        }

        void UpdateUi()
        {
            if (cleanedUp) return;
            DeviceInfo cur = last;
            string glyph;
            string status;
            if (cur == null)
            {
                glyph = Glifos.SpeakerMuted;
                status = "sin salida de audio";
            }
            else if (cur.IsSpeakers)
            {
                float vol; bool mute;
                bool muted = audio != null && audio.TryGetVolume(cur.Id, out vol, out mute) && (mute || vol <= 0.001f);
                glyph = muted ? Glifos.SpeakerMuted : Glifos.Speaker;
                status = "altavoces" + (muted ? " (silenciados)" : "");
            }
            else if (cur.IsHeadphones)
            {
                glyph = Glifos.Headphones;
                status = "audífonos";
            }
            else
            {
                glyph = Glifos.Speaker;
                status = cur.Name;
            }

            tray.Icon = Iconos.Glifo(glyph, !s.Enabled);
            string tip = "Salida: " + status + (s.Enabled ? "" : " — protección desactivada");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 60) + "..." : tip;

            headerItem.Text = "Salida actual: " + (cur == null ? "ninguna" : cur.Name);
            enabledItem.Checked = s.Enabled;
            muteModeItem.Checked = !s.LowVolumeMode;
            lowModeItem.Checked = s.LowVolumeMode;
            alertItem.Checked = s.Alert;
            preItem.Checked = s.PreMute;
            startupItem.Checked = s.Startup;
        }

        void ApplyStartup()
        {
            InicioWindows.Poner(RunName, s.Startup, null);
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            try
            {
                invoker.BeginInvoke((MethodInvoker)delegate { Iconos.LimpiarCache(); UpdateUi(); });
            }
            catch { }
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            // Al apagar: devolver los altavoces como estaban si se silenciaron "por adelantado".
            try { invoker.Invoke((MethodInvoker)delegate { if (last == null || !last.IsSpeakers) RestoreAll(); }); } catch { }
        }

        void Cleanup()
        {
            if (cleanedUp) return;
            try
            {
                if (audio != null && (last == null || !last.IsSpeakers)) RestoreAll();
            }
            catch { }
            cleanedUp = true;
            pollTimer.Stop();
            pendingTimer.Stop();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            Aviso.Cerrar();
            tray.Visible = false;
            tray.Dispose();
            if (audio != null) audio.Dispose();
            audio = null;
        }
    }
}
