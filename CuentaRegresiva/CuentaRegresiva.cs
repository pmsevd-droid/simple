// Cuenta regresiva
// Cuenta el tiempo que falta para una fecha. Vive en los íconos ocultos de la
// barra de tareas (con un ícono que muestra cuánto falta) y, al llegar a cero,
// abre una ventana de celebración con música.
//
// El estilo (ventanas, avisos, menú, íconos, "Acerca de") viene de
// ..\comun\Comun.cs, compartido con las demás utilidades de la colección.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Comun;

[assembly: AssemblyTitle("Cuenta regresiva")]
[assembly: AssemblyDescription("Cuenta regresiva en los íconos ocultos, con celebración al llegar a cero.")]
[assembly: AssemblyProduct("Cuenta regresiva")]
[assembly: AssemblyCompany("Pierol Solano")]
[assembly: AssemblyCopyright("Gratis y de código abierto")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace CuentaRegresiva
{
    // =====================================================================
    //  Configuración (%APPDATA%\CuentaRegresiva\config.ini)
    // =====================================================================
    class Config
    {
        public string Nombre = "¡El gran día!";
        public DateTime Objetivo = new DateTime(2026, 12, 4, 17, 0, 0);
        public int Modo;               // 0 = todo, 1 = días, 2 = horas, 3 = minutos, 4 = segundos
        public bool Musica = true;
        public string Cancion = "";    // vacío = melodía incluida
        public bool Celebrado;         // ya se celebró esta fecha
        public bool AvisoBandeja;      // ya se avisó que sigue en los íconos ocultos

        const string App = "CuentaRegresiva";
        const string FormatoFecha = "yyyy-MM-dd HH:mm:ss";

        public static Config Cargar()
        {
            Config c = new Config();
            Dictionary<string, string> d = Ini.Leer(App);
            if (d == null)
            {
                c.Celebrado = c.Objetivo <= DateTime.Now;
                return c;
            }
            string v;
            if (d.TryGetValue("nombre", out v)) c.Nombre = v;
            DateTime f;
            if (d.TryGetValue("objetivo", out v) &&
                DateTime.TryParseExact(v, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out f))
                c.Objetivo = f;
            int m;
            if (d.TryGetValue("modo", out v) && int.TryParse(v, out m) && m >= 0 && m <= 4) c.Modo = m;
            c.Musica = Ini.Bool(d, "musica", c.Musica);
            if (d.TryGetValue("cancion", out v)) c.Cancion = v;
            c.Celebrado = Ini.Bool(d, "celebrado", false);
            c.AvisoBandeja = Ini.Bool(d, "avisobandeja", false);
            return c;
        }

        public void Guardar()
        {
            Ini.Guardar(App, new string[] {
                "nombre=" + Ini.Linea(Nombre),
                "objetivo=" + Objetivo.ToString(FormatoFecha, CultureInfo.InvariantCulture),
                "modo=" + Modo,
                "musica=" + Ini.B(Musica),
                "cancion=" + Cancion,
                "celebrado=" + Ini.B(Celebrado),
                "avisobandeja=" + Ini.B(AvisoBandeja)
            });
        }
    }

    // =====================================================================
    //  Cálculos y textos de la cuenta
    // =====================================================================
    static class Cuenta
    {
        public static readonly CultureInfo Cultura = CrearCultura();
        public static readonly string[] NombresModo = { "Todo", "Días", "Horas", "Minutos", "Segundos" };

        static CultureInfo CrearCultura()
        {
            try { return new CultureInfo("es-PE"); } catch { }
            try { return new CultureInfo("es"); } catch { }
            return CultureInfo.InvariantCulture;
        }

        // Segundos que faltan (0 si ya pasó)
        public static long Restante(DateTime objetivo)
        {
            double s = (objetivo - DateTime.Now).TotalSeconds;
            return s <= 0 ? 0 : (long)Math.Ceiling(s);
        }

        public static long Valor(long s, int modo)
        {
            switch (modo)
            {
                case 1: return s / 86400;
                case 2: return s / 3600;
                case 3: return s / 60;
                default: return s;
            }
        }

        public static string Unidad(int modo, long v)
        {
            switch (modo)
            {
                case 1: return v == 1 ? "día" : "días";
                case 2: return v == 1 ? "hora" : "horas";
                case 3: return v == 1 ? "minuto" : "minutos";
                default: return v == 1 ? "segundo" : "segundos";
            }
        }

        public static string Num(long v) { return v.ToString("N0", Cultura); }

        public static string Completo(long s)
        {
            return string.Format("{0} d {1:00}:{2:00}:{3:00}", s / 86400, s % 86400 / 3600, s % 3600 / 60, s % 60);
        }

        public static string Corto(long s, int modo)
        {
            if (modo == 0) return Completo(s);
            long v = Valor(s, modo);
            return Num(v) + " " + Unidad(modo, v);
        }

        public static string Fecha(DateTime d)
        {
            string t = d.ToString("dddd, d 'de' MMMM 'de' yyyy · h:mm tt", Cultura);
            return t.Length > 0 ? char.ToUpper(t[0], Cultura) + t.Substring(1) : t;
        }
    }

    // =====================================================================
    //  Música: canción elegida (MP3, WAV...) o melodía festiva generada aquí
    // =====================================================================
    static class Musica
    {
        [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string comando, StringBuilder ret, int largo, IntPtr hwnd);

        static SoundPlayer reproductor;
        static bool mciAbierto;

        public static void Reproducir(string ruta)
        {
            Detener();
            if (!string.IsNullOrEmpty(ruta) && File.Exists(ruta))
            {
                try
                {
                    if (mciSendString("open \"" + ruta + "\" type mpegvideo alias cancioncr", null, 0, IntPtr.Zero) == 0)
                    {
                        mciAbierto = true;
                        if (mciSendString("play cancioncr repeat", null, 0, IntPtr.Zero) == 0) return;
                        if (mciSendString("play cancioncr", null, 0, IntPtr.Zero) == 0) return;
                    }
                }
                catch { }
                Detener();
            }
            try
            {
                reproductor = new SoundPlayer(new MemoryStream(GenerarWav()));
                reproductor.PlayLooping();
            }
            catch { }
        }

        public static void Detener()
        {
            try
            {
                if (reproductor != null) { reproductor.Stop(); reproductor.Dispose(); }
            }
            catch { }
            reproductor = null;
            if (mciAbierto)
            {
                try { mciSendString("close cancioncr", null, 0, IntPtr.Zero); } catch { }
                mciAbierto = false;
            }
        }

        static double Hz(double midi) { return 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0); }

        // Melodía de 12,8 s (32 tiempos a 150 por minuto): melodía, bajo en arpegio y batería.
        public static byte[] GenerarWav()
        {
            // Pares (nota MIDI, duración en tiempos)
            double[] melodia = {
                72, 0.5, 76, 0.5, 79, 0.5, 84, 0.5, 83, 0.5, 84, 0.5, 79, 1, 77, 0.5, 81, 0.5, 84, 0.5,
                81, 0.5, 79, 1, 76, 1, 74, 0.5, 77, 0.5, 81, 0.5, 86, 0.5, 84, 0.5, 83, 0.5, 81, 0.5,
                79, 0.5, 79, 0.5, 83, 0.5, 86, 0.5, 83, 0.5, 84, 2, 72, 0.5, 76, 0.5, 79, 0.5, 84, 0.5,
                83, 0.5, 84, 0.5, 79, 1, 77, 0.5, 81, 0.5, 84, 0.5, 81, 0.5, 79, 1, 76, 1, 77, 0.5,
                76, 0.5, 74, 0.5, 72, 0.5, 74, 1, 79, 1, 84, 1, 79, 0.5, 76, 0.5, 72, 2
            };
            int[] bajos = { 48, 53, 50, 43, 48, 53, 43, 48 };
            int[] arpegio = { 0, 12, 7, 12 };
            const double SR = 22050;
            const double Tiempo = 0.4;
            int total = 282240;
            double[] buf = new double[total];

            // Melodía
            double t0 = 0;
            for (int i = 0; i < melodia.Length; i += 2)
            {
                double f = Hz(melodia[i]);
                double dur = melodia[i + 1] * Tiempo;
                int ini = (int)(t0 * SR);
                int n = (int)(dur * SR);
                for (int k = 0; k < n && ini + k < total; k++)
                {
                    double t = k / SR;
                    double env = Math.Min(1, t / 0.008) * (0.55 + 0.45 * Math.Exp(-t * 8));
                    double cola = (dur - t) / 0.04;
                    if (cola < 1) env *= Math.Max(0, cola);
                    double s = Math.Sin(2 * Math.PI * f * t)
                             + 0.35 * Math.Sin(6 * Math.PI * f * t)
                             + 0.15 * Math.Sin(10 * Math.PI * f * t)
                             + 0.25 * Math.Sin(2 * Math.PI * f * 2 * t);
                    buf[ini + k] += 0.17 * env * s;
                }
                t0 += dur;
            }

            // Bajo (onda triangular)
            for (int c = 0; c < 8; c++)
                for (int j = 0; j < 4; j++)
                {
                    double f = Hz(bajos[c] + arpegio[j]);
                    int ini = (int)((c * 4 + j) * Tiempo * SR);
                    for (int k = 0; k < 8820 && ini + k < total; k++)
                    {
                        double t = k / SR;
                        double fase = f * t % 1.0;
                        double tri = 4 * Math.Abs(fase - 0.5) - 1;
                        double env = Math.Min(1, t / 0.005) * Math.Exp(-t * 5) * Math.Min(1, (0.4 - t) / 0.02);
                        buf[ini + k] += 0.22 * env * tri;
                    }
                }

            // Batería: bombo, caja y platillo
            Random rnd = new Random(7);
            for (int b = 0; b < 64; b++)
            {
                int ini = (int)(b * Tiempo / 2 * SR);
                bool fuerte = b % 2 == 0;
                int pos = b / 2 % 4;
                for (int k = 0; k < 4410 && ini + k < total; k++)
                {
                    double t = k / SR;
                    double ruido = rnd.NextDouble() * 2 - 1;
                    if (fuerte && (pos == 0 || pos == 2))
                    {
                        double f = 45 + 75 * Math.Exp(-t * 30);
                        buf[ini + k] += 0.45 * Math.Exp(-t * 18) * Math.Sin(2 * Math.PI * f * t);
                    }
                    else if (fuerte) buf[ini + k] += 0.16 * Math.Exp(-t * 22) * ruido;
                    else buf[ini + k] += 0.05 * Math.Exp(-t * 70) * ruido;
                }
            }

            // Archivo WAV (16 bits, mono, 22 050 Hz)
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            int bytes = total * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(22050);
            w.Write(44100);
            w.Write((short)2);
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);
            for (int i = 0; i < total; i++) w.Write((short)(Math.Tanh(buf[i] * 1.1) * 30000));
            w.Flush();
            return ms.ToArray();
        }
    }

    // =====================================================================
    //  Colores propios de esta app
    // =====================================================================
    static class Colores
    {
        public static readonly Color Acento = Color.FromArgb(79, 70, 229);
        // Pantalla de la cuenta (tipo reloj digital)
        public static readonly Color Pantalla = Color.FromArgb(20, 36, 60);
        public static readonly Color PantallaCaja = Color.FromArgb(30, 52, 84);
        public static readonly Color PantallaBorde = Color.FromArgb(56, 84, 122);
        public static readonly Color PantallaSuave = Color.FromArgb(160, 182, 210);
        public static readonly Color PantallaDestaque = Color.FromArgb(255, 214, 102);
        // Confeti
        public static readonly Color[] Confeti = {
            Color.FromArgb(250, 204, 21), Color.FromArgb(244, 114, 182), Color.FromArgb(34, 211, 238),
            Color.FromArgb(74, 222, 128), Color.FromArgb(59, 130, 246), Color.FromArgb(251, 146, 60)
        };
    }

    // =====================================================================
    //  Pantalla de la cuenta (parte de arriba de la ventana)
    // =====================================================================
    class PanelCuenta : Control
    {
        static readonly string[] Etiquetas = { "DÍAS", "HORAS", "MINUTOS", "SEGUNDOS" };
        readonly Config cfg;

        public PanelCuenta(Config c)
        {
            cfg = c;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        static float S(float v) { return v * Estilo.Escala; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float W = Width, H = Height;
            g.Clear(Colores.Pantalla);

            using (SolidBrush blanco = new SolidBrush(Color.White))
            using (SolidBrush suave = new SolidBrush(Colores.PantallaSuave))
            {
                float m = S(16);
                string nombre = string.IsNullOrWhiteSpace(cfg.Nombre) ? "Cuenta regresiva" : cfg.Nombre;
                Dibujo.TextoCentrado(g, nombre, Estilo.Letra, FontStyle.Bold, S(18), new RectangleF(m, S(12), W - 2 * m, S(26)), blanco);
                Dibujo.TextoCentrado(g, Cuenta.Fecha(cfg.Objetivo), Estilo.Letra, FontStyle.Regular, S(12), new RectangleF(m, S(38), W - 2 * m, S(18)), suave);

                long s = Cuenta.Restante(cfg.Objetivo);
                RectangleF zona = new RectangleF(m, S(66), W - 2 * m, H - S(66) - S(14));

                if (s <= 0)
                {
                    using (SolidBrush d = new SolidBrush(Colores.PantallaDestaque))
                        Dibujo.TextoCentrado(g, "¡LLEGÓ EL MOMENTO!", Estilo.Letra, FontStyle.Bold, S(30), zona, d);
                    return;
                }

                if (cfg.Modo == 0)
                {
                    // Cuatro casillas: días, horas, minutos, segundos
                    long[] v = { s / 86400, s % 86400 / 3600, s % 3600 / 60, s % 60 };
                    float gap = S(10);
                    float cw = (zona.Width - 3 * gap) / 4;
                    using (SolidBrush caja = new SolidBrush(Colores.PantallaCaja))
                    using (Pen borde = new Pen(Colores.PantallaBorde))
                        for (int i = 0; i < 4; i++)
                        {
                            RectangleF r = new RectangleF(zona.X + i * (cw + gap), zona.Y, cw, zona.Height);
                            g.FillRectangle(caja, r);
                            g.DrawRectangle(borde, r.X, r.Y, r.Width - 1, r.Height - 1);
                            string num = i == 0 ? Cuenta.Num(v[i]) : v[i].ToString("00");
                            Dibujo.TextoCentrado(g, num, "Consolas", FontStyle.Bold, S(44), new RectangleF(r.X + S(4), r.Y + S(4), r.Width - S(8), r.Height - S(28)), blanco);
                            Dibujo.TextoCentrado(g, Etiquetas[i], Estilo.Letra, FontStyle.Bold, S(10), new RectangleF(r.X, r.Bottom - S(26), r.Width, S(20)), suave);
                        }
                }
                else
                {
                    // Un solo número grande
                    long v = Cuenta.Valor(s, cfg.Modo);
                    Dibujo.TextoCentrado(g, Cuenta.Num(v), "Consolas", FontStyle.Bold, S(52), new RectangleF(zona.X, zona.Y - S(4), zona.Width, S(60)), blanco);
                    Dibujo.TextoCentrado(g, Cuenta.Unidad(cfg.Modo, v).ToUpper(Cuenta.Cultura), Estilo.Letra, FontStyle.Bold, S(11), new RectangleF(zona.X, zona.Y + S(54), zona.Width, S(18)), suave);
                    Dibujo.TextoCentrado(g, Cuenta.Completo(s), "Consolas", FontStyle.Regular, S(12), new RectangleF(zona.X, zona.Y + S(72), zona.Width, S(18)), suave);
                }
            }
        }
    }

    // =====================================================================
    //  Ventana de celebración (sin animaciones: confeti quieto y música)
    // =====================================================================
    class Celebracion : VentanaClasica
    {
        readonly string nombre;
        readonly string fecha;
        readonly string cancion;
        readonly bool conMusica;

        public Celebracion(Config cfg) : base("¡Felicidades!")
        {
            nombre = string.IsNullOrWhiteSpace(cfg.Nombre) ? "Llegó el momento" : cfg.Nombre;
            fecha = Cuenta.Fecha(cfg.Objetivo);
            cancion = cfg.Cancion;
            conMusica = cfg.Musica;

            MinimizeBox = false;
            TopMost = true;
            KeyPreview = true;

            int ancho = Estilo.S(480);
            int alto = Estilo.S(220);
            PanelFestivo panel = new PanelFestivo(nombre, fecha);
            panel.SetBounds(0, 0, ancho, alto);
            Controls.Add(panel);

            Button cerrar = Estilo.Boton("¡Gracias! Cerrar");
            cerrar.Click += delegate { Close(); };
            AcceptButton = cerrar;
            CancelButton = cerrar;
            ArmarBarra(ancho, alto, null, new Button[] { cerrar });
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            if (conMusica) Musica.Reproducir(cancion);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Musica.Detener();
            base.OnFormClosed(e);
        }

        class PanelFestivo : Control
        {
            readonly string nombre, fecha;

            public PanelFestivo(string nombre, string fecha)
            {
                this.nombre = nombre;
                this.fecha = fecha;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            static float S(float v) { return v * Estilo.Escala; }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float W = Width, H = Height;
                g.Clear(Estilo.Fondo);

                // Confeti quieto en franjas arriba y abajo (siempre igual: semilla fija)
                Random rnd = new Random(2026);
                for (int i = 0; i < 90; i++)
                {
                    bool arriba = i % 2 == 0;
                    float x = (float)rnd.NextDouble() * W;
                    float y = arriba ? S(6) + (float)rnd.NextDouble() * S(40) : H - S(8) - (float)rnd.NextDouble() * S(34);
                    float t = S(5) + (float)rnd.NextDouble() * S(5);
                    Color c = Colores.Confeti[rnd.Next(Colores.Confeti.Length)];
                    bool circulo = rnd.Next(4) == 0;
                    float rot = (float)rnd.NextDouble() * 360;
                    GraphicsState st = g.Save();
                    g.TranslateTransform(x, y);
                    g.RotateTransform(rot);
                    using (SolidBrush b = new SolidBrush(c))
                    {
                        if (circulo) g.FillEllipse(b, -t / 2.5f, -t / 2.5f, t / 1.25f, t / 1.25f);
                        else g.FillRectangle(b, -t / 2, -t / 4, t, t / 2);
                    }
                    g.Restore(st);
                }

                using (SolidBrush azul = new SolidBrush(Estilo.Titulo))
                using (SolidBrush negro = new SolidBrush(Estilo.Texto))
                using (SolidBrush gris = new SolidBrush(Estilo.TextoSuave))
                {
                    float m = S(20);
                    Dibujo.TextoCentrado(g, "¡FELICIDADES!", Estilo.Letra, FontStyle.Bold, S(40), new RectangleF(m, S(54), W - 2 * m, S(56)), azul);
                    Dibujo.TextoCentrado(g, nombre, Estilo.Letra, FontStyle.Regular, S(20), new RectangleF(m, S(110), W - 2 * m, S(30)), negro);
                    Dibujo.TextoCentrado(g, "Llegó: " + fecha, Estilo.Letra, FontStyle.Regular, S(12), new RectangleF(m, S(142), W - 2 * m, S(20)), gris);
                }
            }
        }
    }

    // =====================================================================
    //  Ventana principal
    // =====================================================================
    class Principal : VentanaClasica
    {
        const string NombreRun = "CuentaRegresiva";

        readonly Config cfg;
        readonly PanelCuenta panel;
        readonly TextBox txtNombre;
        readonly DateTimePicker dtFecha;
        readonly DateTimePicker dtHora;
        readonly RadioButton[] modos = new RadioButton[5];
        readonly CheckBox chkMusica;
        readonly CheckBox chkInicio;
        readonly Label lblCancion;
        readonly LinkLabel lnkIncluida;
        readonly NotifyIcon bandeja;
        readonly System.Windows.Forms.Timer reloj;
        Celebracion celebracion;
        bool saliendo;
        bool cargando = true;
        string ultimaClave = "";
        string ultimoIcono = "";
        Icon iconoBandeja;

        static int S(float v) { return Estilo.S(v); }

        public Principal(Config c) : base("Cuenta regresiva")
        {
            cfg = c;
            SuspendLayout();

            int W = S(500);
            int m = S(Estilo.Margen);

            // ----- Pantalla de la cuenta -----
            panel = new PanelCuenta(cfg);
            panel.SetBounds(0, 0, W, S(170));
            Controls.Add(panel);

            // ----- Grupo "Evento" -----
            int gw = W - 2 * m;
            GroupBox evento = Estilo.Grupo("Evento", m, panel.Bottom + m, gw, S(122));
            evento.Controls.Add(Estilo.Etiqueta("Nombre del evento:", S(12), S(22)));
            txtNombre = new TextBox();
            txtNombre.SetBounds(S(12), S(40), gw - S(24), S(23));
            txtNombre.MaxLength = 40;
            txtNombre.Text = cfg.Nombre;
            txtNombre.TextChanged += delegate { cfg.Nombre = txtNombre.Text; Cambio(); };
            evento.Controls.Add(txtNombre);

            int col2 = S(250);
            evento.Controls.Add(Estilo.Etiqueta("Fecha:", S(12), S(70)));
            evento.Controls.Add(Estilo.Etiqueta("Hora:", col2, S(70)));
            dtFecha = new DateTimePicker();
            dtFecha.SetBounds(S(12), S(88), col2 - S(24), S(23));
            dtFecha.Format = DateTimePickerFormat.Long;
            dtFecha.Value = cfg.Objetivo.Date;
            dtHora = new DateTimePicker();
            dtHora.SetBounds(col2, S(88), gw - col2 - S(12), S(23));
            dtHora.Format = DateTimePickerFormat.Custom;
            dtHora.CustomFormat = "hh:mm tt";
            dtHora.ShowUpDown = true;
            dtHora.Value = cfg.Objetivo;
            dtFecha.ValueChanged += delegate { CambioFecha(); };
            dtHora.ValueChanged += delegate { CambioFecha(); };
            evento.Controls.Add(dtFecha);
            evento.Controls.Add(dtHora);
            Controls.Add(evento);

            // ----- Grupo "Opciones" -----
            GroupBox opciones = Estilo.Grupo("Opciones", m, evento.Bottom + S(Estilo.Espacio), gw, S(150));
            opciones.Controls.Add(Estilo.Etiqueta("Mostrar la cuenta en:", S(12), S(22)));
            int paso = (gw - S(24)) / 5;
            for (int i = 0; i < 5; i++)
            {
                int modo = i;
                RadioButton r = new RadioButton();
                r.Text = Cuenta.NombresModo[i];
                r.AutoSize = true;
                r.Location = new Point(S(12) + i * paso, S(42));
                r.Checked = cfg.Modo == i;
                r.CheckedChanged += delegate
                {
                    if (!r.Checked) return;
                    cfg.Modo = modo;
                    Cambio();
                };
                modos[i] = r;
                opciones.Controls.Add(r);
            }

            chkMusica = new CheckBox();
            chkMusica.Text = "Poner música al llegar a cero";
            chkMusica.AutoSize = true;
            chkMusica.Location = new Point(S(12), S(74));
            chkMusica.Checked = cfg.Musica;
            chkMusica.CheckedChanged += delegate { cfg.Musica = chkMusica.Checked; Cambio(); };
            opciones.Controls.Add(chkMusica);

            Button elegir = Estilo.Boton("Elegir canción…");
            elegir.Location = new Point(gw - S(12) - elegir.Width, S(70));
            elegir.Click += delegate { ElegirCancion(); };
            opciones.Controls.Add(elegir);

            lblCancion = new Label();
            lblCancion.SetBounds(S(30), S(98), S(280), S(18));
            lblCancion.ForeColor = Estilo.TextoSuave;
            lblCancion.AutoEllipsis = true;
            opciones.Controls.Add(lblCancion);

            lnkIncluida = new LinkLabel();
            lnkIncluida.Text = "usar la melodía incluida";
            lnkIncluida.SetBounds(gw - S(12) - S(160), S(98), S(160), S(18));
            lnkIncluida.TextAlign = ContentAlignment.TopRight;
            lnkIncluida.LinkColor = Estilo.Enlace;
            lnkIncluida.LinkClicked += delegate
            {
                cfg.Cancion = "";
                MostrarCancion();
                Cambio();
            };
            opciones.Controls.Add(lnkIncluida);

            chkInicio = new CheckBox();
            chkInicio.Text = "Abrir con Windows (directo a los íconos ocultos)";
            chkInicio.AutoSize = true;
            chkInicio.Location = new Point(S(12), S(122));
            chkInicio.Checked = InicioWindows.Activo(NombreRun);
            chkInicio.CheckedChanged += delegate
            {
                if (!cargando) InicioWindows.Poner(NombreRun, chkInicio.Checked, "--bandeja");
            };
            opciones.Controls.Add(chkInicio);
            Controls.Add(opciones);

            // ----- Barra de botones -----
            Button probar = Estilo.Boton("Probar celebración");
            probar.Click += delegate { Celebrar(false); };
            Button listo = Estilo.Boton("Listo, mandar a los íconos ocultos");
            listo.Click += delegate { Ocultar(); };
            ArmarBarra(W, opciones.Bottom + m, new Button[] { probar }, new Button[] { listo });

            MostrarCancion();
            ResumeLayout(false);

            // ----- Ícono de la bandeja -----
            ContextMenuStrip menu = MenuClasico.Crear();
            menu.Items.Add(MenuClasico.Principal("Abrir cuenta regresiva", delegate { Mostrar(); }));
            menu.Items.Add(MenuClasico.Opcion("Probar celebración", delegate { Celebrar(false); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuClasico.Opcion("Acerca de…", delegate { AcercaDe.Mostrar(); }));
            menu.Items.Add(MenuClasico.Opcion("Salir", delegate { Salir(); }));

            bandeja = new NotifyIcon();
            bandeja.ContextMenuStrip = menu;
            bandeja.Text = "Cuenta regresiva";
            iconoBandeja = Iconos.Insignia("…", Colores.Acento);
            bandeja.Icon = iconoBandeja;
            bandeja.Visible = true;
            bandeja.DoubleClick += delegate { Mostrar(); };

            // Si ya estaba en el inicio de Windows, actualizar la ruta por si se movió
            if (chkInicio.Checked) InicioWindows.Poner(NombreRun, true, "--bandeja");
            cargando = false;

            reloj = new System.Windows.Forms.Timer();
            reloj.Interval = 200;
            reloj.Tick += delegate { Actualizar(); };
            reloj.Start();
            Actualizar();

            IntPtr h = Handle; // crea la ventana aunque empiece oculta (para poder mostrarla desde otra instancia)
            GC.KeepAlive(h);
        }

        void MostrarCancion()
        {
            bool propia = !string.IsNullOrEmpty(cfg.Cancion);
            lblCancion.Text = propia ? "♪ " + Path.GetFileName(cfg.Cancion) : "♪ Melodía festiva incluida";
            lnkIncluida.Visible = propia;
        }

        void ElegirCancion()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Elige la canción para la celebración";
                d.Filter = "Audio (*.mp3;*.wav;*.wma;*.m4a)|*.mp3;*.wav;*.wma;*.m4a|Todos los archivos (*.*)|*.*";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    cfg.Cancion = d.FileName;
                    cfg.Musica = true;
                    chkMusica.Checked = true;
                    MostrarCancion();
                    Cambio();
                }
            }
        }

        void CambioFecha()
        {
            if (cargando) return;
            DateTime h = dtHora.Value;
            cfg.Objetivo = dtFecha.Value.Date + new TimeSpan(h.Hour, h.Minute, 0);
            cfg.Celebrado = cfg.Objetivo <= DateTime.Now;
            Cambio();
        }

        void Cambio()
        {
            if (cargando) return;
            cfg.Guardar();
            ultimaClave = "";
            Actualizar();
        }

        // Se llama 5 veces por segundo, pero solo redibuja si algo cambió
        void Actualizar()
        {
            long s = Cuenta.Restante(cfg.Objetivo);
            if (s <= 0 && !cfg.Celebrado)
            {
                cfg.Celebrado = true;
                cfg.Guardar();
                Celebrar(true);
            }

            string clave = s + "|" + cfg.Modo + "|" + cfg.Nombre;
            if (clave == ultimaClave) return;
            ultimaClave = clave;
            panel.Invalidate();

            string nombre = string.IsNullOrWhiteSpace(cfg.Nombre) ? "Cuenta regresiva" : cfg.Nombre;
            if (nombre.Length > 28) nombre = nombre.Substring(0, 27) + "…";
            string tip = s <= 0 ? nombre + "\n¡Llegó el momento!" : nombre + "\nFaltan " + Cuenta.Corto(s, cfg.Modo);
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            bandeja.Text = tip;

            // Ícono: días (o horas/minutos/segundos si falta poco), con color según lo que falta
            string texto;
            Color color;
            if (s <= 0) { texto = "!"; color = Color.FromArgb(22, 163, 74); }
            else if (s >= 86400)
            {
                texto = (s / 86400).ToString();
                color = s < 604800 ? Color.FromArgb(234, 88, 12) : Colores.Acento;
            }
            else if (s >= 3600) { texto = s / 3600 + "h"; color = Color.FromArgb(220, 38, 38); }
            else if (s >= 60) { texto = s / 60 + "m"; color = Color.FromArgb(220, 38, 38); }
            else { texto = s.ToString(); color = Color.FromArgb(220, 38, 38); }
            if (texto.Length > 3) texto = "99+";

            string claveIcono = texto + color.ToArgb();
            if (claveIcono != ultimoIcono)
            {
                ultimoIcono = claveIcono;
                Icon anterior = iconoBandeja;
                iconoBandeja = Iconos.Insignia(texto, color);
                bandeja.Icon = iconoBandeja;
                if (anterior != null) anterior.Dispose();
            }
        }

        void Celebrar(bool real)
        {
            if (celebracion != null && !celebracion.IsDisposed) { celebracion.Activate(); return; }
            celebracion = new Celebracion(cfg);
            celebracion.FormClosed += delegate { celebracion = null; };
            celebracion.Show();
            if (real)
                Aviso.Mostrar("¡Felicidades!",
                    string.IsNullOrWhiteSpace(cfg.Nombre) ? "Llegó el momento" : cfg.Nombre,
                    Iconos.App, null, null, Mostrar);
        }

        public void Mostrar()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void Ocultar()
        {
            Hide();
            if (!cfg.AvisoBandeja)
            {
                cfg.AvisoBandeja = true;
                cfg.Guardar();
                Aviso.Mostrar("Sigo contando aquí",
                    "La cuenta regresiva está en los íconos ocultos (flechita ^). Doble clic para abrirla, clic derecho para salir.",
                    SystemIcons.Information, null, null, Mostrar);
            }
        }

        void Salir()
        {
            saliendo = true;
            reloj.Stop();
            Musica.Detener();
            Aviso.Cerrar();
            bandeja.Visible = false;
            bandeja.Dispose();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!saliendo && e.CloseReason == CloseReason.UserClosing)
            {
                // La X no cierra el programa: lo manda a los íconos ocultos
                e.Cancel = true;
                Ocultar();
                return;
            }
            if (!saliendo) bandeja.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (!saliendo)
            {
                saliendo = true;
                Application.Exit();
            }
        }
    }

    // =====================================================================
    //  Programa principal (una sola copia abierta a la vez)
    // =====================================================================
    static class Program
    {
        const string NombreMutex = "CuentaRegresiva_Pierol_Instancia";
        const string NombreEvento = "CuentaRegresiva_Pierol_Mostrar";

        [STAThread]
        static void Main(string[] args)
        {
            bool nueva;
            using (Mutex mutex = new Mutex(true, NombreMutex, out nueva))
            {
                if (!nueva)
                {
                    // Ya está abierta: pedirle que muestre su ventana
                    try
                    {
                        using (EventWaitHandle ev = EventWaitHandle.OpenExisting(NombreEvento)) ev.Set();
                    }
                    catch { }
                    return;
                }

                Estilo.Preparar();
                Principal ventana = new Principal(Config.Cargar());

                EventWaitHandle mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, NombreEvento);
                ThreadPool.RegisterWaitForSingleObject(mostrar, delegate
                {
                    try { ventana.BeginInvoke((MethodInvoker)ventana.Mostrar); } catch { }
                }, null, -1, false);

                bool enBandeja = Array.Exists(args, delegate (string a)
                {
                    return a.Equals("--bandeja", StringComparison.OrdinalIgnoreCase);
                });
                if (!enBandeja) ventana.Show();

                Application.Run();
                GC.KeepAlive(mutex);
            }
        }
    }
}
