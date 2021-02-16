using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TestVideoClient
{

    public partial class Form1 : Form
    {
        private List<Camera> cameras;
        private Camera currentCam;
        private Bitmap currentFrame;

        public Form1()
        {
            InitializeComponent();

            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint, true);
            UpdateStyles();

            Camera.Error += ShowError;
            InitCameraList();
        }

        private void ImageReady(Bitmap image)
        {
            currentFrame = image;
            try
            {
                this.Invoke(new Action(() => this.Refresh()));
            }
            catch (ObjectDisposedException)
            {
                currentCam?.StopStream();
            }
        }

        private void ShowError(string error)
        {
            MessageBox.Show(error, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private async Task InitCameraList()
        {
            while (true)
            {
                try
                {
                    cameras = await Camera.GetCameraList();
                    break;
                }
                catch (WebException e)
                {
                    ShowError(e.Message);
                }
            }
            listBox1.DataSource = cameras;
            listBox1.DisplayMember = "Name";
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (currentCam != null)
            {
                currentCam.StopStream();
                currentCam.ImageReady -= ImageReady;
            }
            if (listBox1.SelectedIndex == -1)
                return;
            currentCam = (Camera)listBox1.SelectedItem;
            currentCam.ImageReady += ImageReady;
            currentCam.StartStream();
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            if (currentFrame == null)
                return;

            e.Graphics.DrawImageUnscaled(currentFrame, 218, 12);
        }

    }
}

