#ifndef SP_BRUSH_EFFECTS
#define SP_BRUSH_EFFECTS

struct SegmentResult {
    float2 nearestPoint;
    float2 interpolatedSize;
    float interpolatedStrength;
};


//  Returns point on segment and also interpolated size and strength
// (from two values at the ends of the segment). Handles degenerate segment (A==B) as single point.
SegmentResult nearestPointOnSegment(float2 fromPoint, float2 segmentPointA, float2 segmentPointB, 
                                    float2 sizeA, float2 sizeB, float strengthA, float strengthB, float aspect){
    float2 normSegmentPointA = float2(segmentPointA.x * aspect, segmentPointA.y);
    float2 normSegmentPointB = float2(segmentPointB.x * aspect, segmentPointB.y);
    float2 normP = float2(fromPoint.x*aspect, fromPoint.y);

    float2 ab = normSegmentPointB - normSegmentPointA;
    float2 ap = normP - normSegmentPointA;
    float ab2 = dot(ab, ab);

    SegmentResult result;
    if (ab2 < 1e-8) {
        result.nearestPoint = segmentPointA;
        result.interpolatedSize = sizeA;
        result.interpolatedStrength = strengthA;
        return result;
    }
    float t = dot(ap, ab) / ab2;
    t = saturate(t);
    float2 normNearestPoint = normSegmentPointA + t * ab;
    result.nearestPoint = float2(normNearestPoint.x / aspect, normNearestPoint.y);
    result.interpolatedSize = lerp(sizeA, sizeB, t);
    result.interpolatedStrength = lerp(strengthA, strengthB, t);
    return result;
}



struct PaintInBrushStroke_Input{
    float screenAspectRatio;
    //screen space position [0,0] to [1,1] of the current fragment.
    float2 fragScreenSpaceUV;
    //xy: previous brush coord in the viewport (during previous frame).  
    //zy: this frame brush coord.
    float4 PrevNewBrushScreenCoord; 
    //x: prev brush size (maybe pressure was different). 
    //y: this frame brush size.  z: 0,  w: is it first frame of painting the stroke.
    float4 BrushSizes_andFirstFrameFlag;
    //black and white image of brush (circular gradient, etc):
    sampler2D BrushStamp;
    
    // 0 when erasing a painted color mask
    // 0.5 when Erasing a projector mask.
    // This helps to make them similar, because colors are erasing very strongly, and brush looks thicker
    float brushStampStronger;
    
    float2 BrushStrength01; // Can be between 0 to 1.  x: previous, y: new
    
    //alpha where brush already painted during the current stroke. 
    //Allows to prevent ""building up" of color. Usually looks like a "worm" on the gray texture.
    // Can be beteween 0 to 1
    float currentBrushPath01; 
    
    // we'll diminish brushing for surfaces that face away from the camera.
    float normalDotView;

    // Brush angle in radians (0 = no rotation). For directional alphas.
    float brushAngleRad;
    // Brush roundness 0-1 (1 = circle). For elliptical tips.
    float brushRoundness01;
    // 0 = symmetry off. 1 = screen mirror (x' = 1 - x). 2 = mesh mirror: use MirrorPrevNewBrushScreenCoord (xy=prev, zw=new).
    float symmetryMode;
    float4 MirrorPrevNewBrushScreenCoord;
    // Additional rotation (radians) applied only on mirrored side, so directional tips align to mirrored stroke direction.
    float symmetryMirrorAngleDeltaRad;
};




float invLerp(float a, float b, float x){
    return (x-a)/(b-a);
}

// Rotate 2D vector by angle (radians).
float2 rotate2d(float2 v, float rad){
    float c = cos(rad), s = sin(rad);
    return float2(v.x*c - v.y*s, v.x*s + v.y*c);
}

// Compute brush stamp UV from fragment position, center, size, with angle and roundness. Unclamped: outside [0,1] means outside the stamp footprint (caller must not sample the texture there — avoids clamp-to-edge line artifacts from non-zero border texels).
float2 brushStampUV(float2 fragUV, float2 center, float2 size, float aspect, float angleRad, float roundness01){
    float2 d = fragUV - center;
    d = rotate2d(d, -angleRad);
    size.x /= aspect;
    float ry = max(0.01, roundness01);
    size.y = size.x * ry;
    return (d + 0.5*size) / size;
}

// Single-stamp sample with angle/roundness and strength curve.
float sampleBrushStamp(PaintInBrushStroke_Input i, float2 uv, float strength){
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 0.0;
    float brushStamp = tex2D(i.BrushStamp, uv).r;
    brushStamp = 1.0 - pow(1.0 - brushStamp, 1.0 + i.brushStampStronger);
    brushStamp = saturate(brushStamp);
    float normDotView = saturate(invLerp(0.2, 0.5, i.normalDotView));
    return brushStamp * strength * normDotView;
}

// Continuous segment (worm) between prev and new position. Uses angle and roundness for stamp shape.
float PaintInBrushStroke(PaintInBrushStroke_Input i){
    float2 brushPrevPos =  i.PrevNewBrushScreenCoord.xy;
    float2 brushNewPos =   i.PrevNewBrushScreenCoord.zw;
    float2 brushPrevSize = i.BrushSizes_andFirstFrameFlag.xx; 
    float2 brushNewSize =  i.BrushSizes_andFirstFrameFlag.yy;
    float brushPrevStrength = i.BrushStrength01.x;
    float brushNewStrength =  i.BrushStrength01.y;

    SegmentResult segResult = nearestPointOnSegment( i.fragScreenSpaceUV, brushPrevPos, brushNewPos, 
                                                     brushPrevSize, brushNewSize, brushPrevStrength, brushNewStrength, i.screenAspectRatio);
    float2 brushNearPos = segResult.nearestPoint;
    float2 brushNearSize = segResult.interpolatedSize;
    float brushNearStrength = segResult.interpolatedStrength;

    float angleRad = i.brushAngleRad;
    float roundness01 = i.brushRoundness01 > 0.0 ? i.brushRoundness01 : 1.0;
    float2 brushUV_curr = brushStampUV(i.fragScreenSpaceUV, brushNearPos, brushNearSize, i.screenAspectRatio, angleRad, roundness01);
    float wanted = sampleBrushStamp(i, brushUV_curr, brushNearStrength);
    float outv = max(wanted, i.currentBrushPath01);
    if (i.symmetryMode < 0.5)
        return outv;

    float2 mPrev = (i.symmetryMode < 1.5)
        ? float2(1.0 - brushPrevPos.x, brushPrevPos.y)
        : i.MirrorPrevNewBrushScreenCoord.xy;
    float2 mNew = (i.symmetryMode < 1.5)
        ? float2(1.0 - brushNewPos.x, brushNewPos.y)
        : i.MirrorPrevNewBrushScreenCoord.zw;
    SegmentResult segM = nearestPointOnSegment(i.fragScreenSpaceUV, mPrev, mNew,
        brushPrevSize, brushNewSize, brushPrevStrength, brushNewStrength, i.screenAspectRatio);
    float angleRadM = angleRad + i.symmetryMirrorAngleDeltaRad;
    float2 brushUV_m = brushStampUV(i.fragScreenSpaceUV, segM.nearestPoint, segM.interpolatedSize, i.screenAspectRatio, angleRadM, roundness01);
    float wantedM = sampleBrushStamp(i, brushUV_m, segM.interpolatedStrength);
    return max(outv, wantedM);
}

// Splotch mode: discrete stamps along the path. stampPosSizeStr[k] = (pos.x, pos.y, size, strength). stampCount 0 = use segment.
float PaintInBrushStroke_Splotches(PaintInBrushStroke_Input i, float4 stampPosSizeStr[64], int stampCount){
    if (stampCount <= 0)
        return PaintInBrushStroke(i);
    float angleRad = i.brushAngleRad;
    float roundness01 = i.brushRoundness01 > 0.0 ? i.brushRoundness01 : 1.0;
    float accum = i.currentBrushPath01;
    for (int k = 0; k < stampCount; k++){
        float2 center = stampPosSizeStr[k].xy;
        float size = stampPosSizeStr[k].z;
        float strength = stampPosSizeStr[k].w;
        float2 size2 = float2(size, size);
        float2 uv = brushStampUV(i.fragScreenSpaceUV, center, size2, i.screenAspectRatio, angleRad, roundness01);
        float w = sampleBrushStamp(i, uv, strength);
        accum = max(accum, w);
    }
    // Screen mirror only (mode 1). Mesh mirror duplicates stamps in C# and uses mode 0 here.
    if (i.symmetryMode > 0.5 && i.symmetryMode < 1.5) {
        for (int km = 0; km < stampCount; km++){
            float2 centerM = float2(1.0 - stampPosSizeStr[km].x, stampPosSizeStr[km].y);
            float sizeM = stampPosSizeStr[km].z;
            float strengthM = stampPosSizeStr[km].w;
            float2 size2M = float2(sizeM, sizeM);
            float angleRadM = angleRad + i.symmetryMirrorAngleDeltaRad;
            float2 uvM = brushStampUV(i.fragScreenSpaceUV, centerM, size2M, i.screenAspectRatio, angleRadM, roundness01);
            float wM = sampleBrushStamp(i, uvM, strengthM);
            accum = max(accum, wM);
        }
    }
    return accum;
}


// Cursor mask at current brush position (with angle/roundness).
float Mask_by_CurrBrushCursor(PaintInBrushStroke_Input i){
    float2 brushNewPos = i.PrevNewBrushScreenCoord.zw;
    float2 brushNewSize = i.BrushSizes_andFirstFrameFlag.yy;
    float angleRad = i.brushAngleRad;
    float roundness01 = i.brushRoundness01 > 0.0 ? i.brushRoundness01 : 1.0;
    float2 brushUV_curr = brushStampUV(i.fragScreenSpaceUV, brushNewPos, brushNewSize, i.screenAspectRatio, angleRad, roundness01);
    float a = 0.0;
    if (brushUV_curr.x >= 0.0 && brushUV_curr.x <= 1.0 && brushUV_curr.y >= 0.0 && brushUV_curr.y <= 1.0)
        a = tex2D(i.BrushStamp, brushUV_curr).r;
    if (i.symmetryMode < 0.5)
        return a;
    float2 mPos = (i.symmetryMode < 1.5)
        ? float2(1.0 - brushNewPos.x, brushNewPos.y)
        : i.MirrorPrevNewBrushScreenCoord.zw;
    float angleRadM = angleRad + i.symmetryMirrorAngleDeltaRad;
    float2 brushUV_m = brushStampUV(i.fragScreenSpaceUV, mPos, brushNewSize, i.screenAspectRatio, angleRadM, roundness01);
    if (brushUV_m.x >= 0.0 && brushUV_m.x <= 1.0 && brushUV_m.y >= 0.0 && brushUV_m.y <= 1.0)
        a = max(a, tex2D(i.BrushStamp, brushUV_m).r);
    return a;
}

#endif