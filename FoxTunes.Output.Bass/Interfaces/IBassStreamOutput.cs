﻿namespace FoxTunes.Interfaces
{
    public interface IBassStreamOutput : IBassStreamControllable, IBassStreamComponent
    {
        bool IsPlaying { get; }

        bool IsPaused { get; }

        bool IsStopped { get; }

        bool CheckFormat(int rate, int channels);

        bool CanGetData { get; }

        int GetData(short[] buffer);

        int GetData(float[] buffer);

        int GetData(float[] buffer, int fftSize);
    }
}
