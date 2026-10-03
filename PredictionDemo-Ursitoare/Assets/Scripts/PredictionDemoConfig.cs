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

        //FOLLOWERS (entities not controlled by this client: other players, balls)
        //NOTE: library default ignores resim decisions of controllable followers, so other players drift until the local entity triggers a resim.
        public bool resim_ignore_follower_decisions = false;
        public bool ignore_non_auth_resim_decisions = false;
        public bool client_apply_server_input_to_followers = true;
        public float resim_followers_distance_treshold = 0f;
        public float precise_resim_followers_distance_treshold = 3f;
        public float follower_dist_tres = 0.201f;
        public float follower_rot_tres = 0.201f;
        public float follower_velo_tres = 0.201f;
        public float follower_avelo_tres = 0.201f;
    }
}
