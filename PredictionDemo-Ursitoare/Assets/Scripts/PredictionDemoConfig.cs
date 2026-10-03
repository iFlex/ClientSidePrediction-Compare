using System;

namespace DefaultNamespace
{
    //NOTE: defaults mirror AntShipWars' MasterConfig.json predictionConfig so both projects tick the same way.
    [Serializable]
    public class PredictionDemoConfig
    {
        //TIMING
        public int SimulationHz = 120;
        public int RenderingHz = 120;
        public int NetworkHz = 60;

        //SERVER
        public bool server_use_buffering = true;
        public int server_buffer_size = 5;
        public bool server_catchup = true;
        public int server_catchup_sections = 10;
        public bool server_increment_ticks = false;

        //CLIENT RESIMULATION
        public bool resimulate = true;
        public bool snap = false;
        public bool oversim_protect = false;
        public bool oversim_protect_with_tick_interval = true;
        public uint oversim_min_ticks_between = 5;
        public uint max_tick_resim_count = 1;
        public float dist_tres = 0.01f;
        public float rot_tres = 0.01f;
        public float velo_tres = 0.01f;
        public float avelo_tres = 0.01f;
    }
}
