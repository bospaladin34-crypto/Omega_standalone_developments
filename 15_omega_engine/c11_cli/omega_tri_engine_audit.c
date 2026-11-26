/*
 * Station 15: omega_tri_engine_audit.c (Universal C11 Mathematical & ABI Verifier)
 * Compile: gcc -O3 -std=c11 -Wall -Wextra -Werror 15_omega_engine/c11_cli/omega_tri_engine_audit.c -lm -o omega_tri_engine_audit
 */
#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define OMEGA_TRI_MAGIC 0x4F545249u

typedef struct OmegaTriUniforms320 {
    float U[20][4];
} OmegaTriUniforms320;

typedef struct OmegaTriSeqlockTrailer64 {
    uint32_t magic;
    uint32_t version;
    uint64_t sequence;
    uint32_t active_mask;
    uint32_t payload_crc32;
    uint64_t qpc_ticks;
    uint64_t reserved[4];
} OmegaTriSeqlockTrailer64;

typedef struct OmegaTriMmfPacket384 {
    OmegaTriUniforms320 uniforms;
    OmegaTriSeqlockTrailer64 trailer;
} OmegaTriMmfPacket384;

static uint32_t crc32_ieee(const uint8_t *buf, size_t len) {
    uint32_t crc = 0xFFFFFFFFu;
    for (size_t i = 0; i < len; i++) {
        crc ^= (uint32_t)buf[i];
        for (int b = 0; b < 8; b++) {
            crc = (crc & 1u) ? (0xEDB88320u ^ (crc >> 1)) : (crc >> 1);
        }
    }
    return ~crc;
}

static float om_d8_dist2(const float x[8], float sh, uint32_t *chash) {
    float u[8], e[8];
    int z[8];
    int sum = 0;
    int worst_idx = 0;
    float worst_abs = -1.0f;
    for (int i = 0; i < 8; i++) {
        u[i] = x[i] - sh;
        z[i] = (int)floorf(u[i] + 0.5f);
        e[i] = u[i] - (float)z[i];
        sum += z[i];
        float a = fabsf(e[i]);
        if (a > worst_abs) { worst_abs = a; worst_idx = i; }
    }
    if ((sum & 1) != 0) {
        z[worst_idx] += (e[worst_idx] >= 0.0f) ? 1 : -1;
        e[worst_idx] = u[worst_idx] - (float)z[worst_idx];
    }
    float d2 = 0.0f;
    uint32_t h = 2166136261u;
    for (int i = 0; i < 8; i++) {
        d2 += e[i] * e[i];
        h = (h ^ (uint32_t)(z[i] + 1024)) * 16777619u;
    }
    *chash = h;
    return d2;
}

static float om_sdGyroid(float x, float y, float z, float sc) {
    float s = fmaxf(0.05f, fabsf(sc));
    return (sinf(x * s) * cosf(y * s) + sinf(y * s) * cosf(z * s) + sinf(z * s) * cosf(x * s)) / s;
}

int main(void) {
    if (sizeof(OmegaTriUniforms320) != 320u) return 1;
    if (sizeof(OmegaTriSeqlockTrailer64) != 64u) return 2;
    if (sizeof(OmegaTriMmfPacket384) != 384u) return 3;
    if (offsetof(OmegaTriMmfPacket384, trailer) != 320u) return 4;
    if (offsetof(OmegaTriSeqlockTrailer64, sequence) != 8u) return 5;
    if (offsetof(OmegaTriSeqlockTrailer64, payload_crc32) != 20u) return 6;

    const char *vec = "123456789";
    if (crc32_ieee((const uint8_t *)vec, 9u) != 0xCBF43926u) return 7;

    float spinor[8] = {0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f};
    uint32_t h0 = 0, h1 = 0;
    float d0 = om_d8_dist2(spinor, 0.0f, &h0);
    float d1 = om_d8_dist2(spinor, 0.5f, &h1);
    if (!(d1 < 1e-6f && d0 > 0.99f)) return 8;

    float g = om_sdGyroid(1.1f, -0.7f, 2.3f, 4.5f);
    if (!isfinite(g)) return 9;

    printf("[OMEGA TRI-ENGINE C11 ABI & MATH AUDIT] 9/9 PASS | cbuffer=320B | trailer=64B | total=384B | CRC32=0xCBF43926 | E8_d1=%.6f\n", d1);
    return 0;
}