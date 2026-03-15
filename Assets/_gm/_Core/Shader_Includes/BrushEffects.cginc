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
};




float invLerp(float a, float b, float x){
    return (x-a)/(b-a);
}

// Rotate 2D vector by angle (radians).
float2 rotate2d(float2 v, float rad){
    float c = cos(rad), s = sin(rad);
    return float2(v.x*c - v.y*s, v.x*s + v.y*c);
}

// Compute brush stamp UV from fragment position, center, size, with angle and roundness. Returns UV in [0,1].
float2 brushStampUV(float2 fragUV, float2 center, float2 size, float aspect, float angleRad, float roundness01){
    float2 d = fragUV - center;
    d = rotate2d(d, -angleRad);
    size.x /= aspect;
    float ry = max(0.01, roundness01);
    size.y = size.x * ry;
    float2 uv = (d + 0.5*size) / size;
    return clamp(uv, 0.0, 1.0);
}

// Single-stamp sample with angle/roundness and strength curve.
float sampleBrushStamp(PaintInBrushStroke_Input i, float2 uv, float strength){
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
    return max(wanted, i.currentBrushPath01);
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
    return accum;
}


// Cursor mask at current brush position (with angle/roundness).
float Mask_by_CurrBrushCursor(PaintInBrushStroke_Input i){
    float2 brushNewPos = i.PrevNewBrushScreenCoord.zw;
    float2 brushNewSize = i.BrushSizes_andFirstFrameFlag.yy;
    float angleRad = i.brushAngleRad;
    float roundness01 = i.brushRoundness01 > 0.0 ? i.brushRoundness01 : 1.0;
    float2 brushUV_curr = brushStampUV(i.fragScreenSpaceUV, brushNewPos, brushNewSize, i.screenAspectRatio, angleRad, roundness01);
    return tex2D(i.BrushStamp, brushUV_curr).r;
}

#endif