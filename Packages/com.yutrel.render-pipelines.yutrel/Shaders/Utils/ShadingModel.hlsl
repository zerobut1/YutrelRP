#ifndef YUTREL_SHADING_MODEL_INCLUDED
#define YUTREL_SHADING_MODEL_INCLUDED

#define SHADING_MODEL_NONE 0
#define SHADING_MODEL_STANDARD 1
#define SHADING_MODEL_ENDFIELD 2
#define SHADING_MODEL_OPENPBR 3

float EncodeShadingModelID(int shading_model_id)
{
    return saturate((float)shading_model_id / 255.0f);
}

int DecodeShadingModelID(float encoded_shading_model_id)
{
    return (int)round(saturate(encoded_shading_model_id) * 255.0f);
}

bool ShadingModelUsesDeferredLighting(int shading_model_id)
{
    return shading_model_id == SHADING_MODEL_STANDARD ||
           shading_model_id == SHADING_MODEL_OPENPBR;
}

bool ShadingModelHasSurfaceNormal(int shading_model_id)
{
    return shading_model_id == SHADING_MODEL_STANDARD ||
           shading_model_id == SHADING_MODEL_ENDFIELD ||
           shading_model_id == SHADING_MODEL_OPENPBR;
}

#endif
