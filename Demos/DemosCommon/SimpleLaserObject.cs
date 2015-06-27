﻿	using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using SimpleScene.Util;

namespace SimpleScene
{
	// TODO have an easy way to update laser start and finish
	// TODO ability to "hold" laser active

	// TODO pulse, interference effects
	// TODO laser "drift"

	public class SSLaserParameters
	{
		public Color4 backgroundColor = Color4.White;
		public Color4 overlayColor = Color4.White;
		/// <summary>
		/// width of the middle section sprite (in world units)
		/// </summary>
		public float backgroundWidth = 0.1f;
		/// <summary>
		/// ratio, 0-1, of how much of the background is covered by overlay
		/// </summary>
		public float overlayWidthRatio = 0.15f;
		/// <summary>
		/// start (emission) sprite will be drawn x times larger than the middle section
		/// </summary>
		public float startPointScale = 1.2f; 
	}

	public class SSLaser
	{
		public Vector3 start;
		public Vector3 end;
		public SSLaserParameters parameters;

		public float laserOpacity;
	}

	public class SimpleLaserObject : SSObject
	{
		public SSLaser laser = null;
		public SSTexture middleBackgroundSprite = null;
		public SSTexture middleOverlaySprite = null;
		public SSTexture startBackgroundSprite = null;
		public SSTexture startOverlaySprite = null;

		static readonly protected UInt16[] _middleIndices = {
			0,1,2, 1,3,2, // left cap
			2,3,4, 3,5,4, // middle
			//4,5,6, 5,7,6  // right cap
		};
		protected SSVertex_PosTex[] _middleVertices;
		protected SSIndexedMesh<SSVertex_PosTex> _middleMesh;

		// TODO cache these computations
		public override Vector3 localBoundingSphereCenter {
			get {
				if (laser == null) {
					return Vector3.Zero;
				}
				Vector3 middleWorld = (laser.start + laser.end) / 2f;
				return Vector3.Transform (middleWorld, this.worldMat.Inverted ());
			}
		}

		// TODO cache these computations
		public override float localBoundingSphereRadius {
			get {
				if (laser == null) {
					return 0f;
				}
				Vector3 diff = (laser.end - laser.start);
				return diff.LengthFast/2f;
			}
		}

		public SimpleLaserObject (SSLaser laser = null, 
							      SSTexture middleBackgroundSprite = null,
								  SSTexture middleOverlaySprite = null,
								  SSTexture startBackgroundSprite = null,
								  SSTexture startOverlaySprite = null)
		{
			this.laser = laser;

			this.renderState.castsShadow = false;
			this.renderState.receivesShadows = false;

			var ctx = new SSAssetManager.Context ("./lasers");
			this.middleBackgroundSprite = middleBackgroundSprite 
				?? SSAssetManager.GetInstance<SSTextureWithAlpha>(ctx, "background.png");
			this.middleOverlaySprite = middleOverlaySprite
				?? SSAssetManager.GetInstance<SSTextureWithAlpha>(ctx, "background.png");
			this.startBackgroundSprite = startBackgroundSprite
				?? SSAssetManager.GetInstance<SSTextureWithAlpha>(ctx, "background.png");
			this.startOverlaySprite = startOverlaySprite
				?? SSAssetManager.GetInstance<SSTextureWithAlpha>(ctx, "background.png");

			// reset all mat colors. emission will be controlled during rendering
			this.AmbientMatColor = new Color4(0f, 0f, 0f, 0f);
			this.DiffuseMatColor = new Color4(0f, 0f, 0f, 0f);
			this.SpecularMatColor = new Color4(0f, 0f, 0f, 0f);
			this.EmissionMatColor = new Color4(0f, 0f, 0f, 0f);

			// initialize non-changing vertex data
			_initMiddleMesh ();
		}

		public override void Render(SSRenderConfig renderConfig)
		{
			base.Render (renderConfig);

			// step: setup render settings
			SSShaderProgram.DeactivateAll ();
			GL.ActiveTexture (TextureUnit.Texture0);
			GL.Enable (EnableCap.Texture2D);

			GL.Enable(EnableCap.Blend);
			GL.BlendFunc (BlendingFactorSrc.SrcAlpha, BlendingFactorDest.One);

			// step: compute endpoints in view space
			var startView = Vector3.Transform(laser.start, renderConfig.invCameraViewMatrix);
			var endView = Vector3.Transform (laser.end, renderConfig.invCameraViewMatrix);
			var middleView = (startView + endView) / 2f;

			// step: draw middle section:
			Vector3 diff = endView - startView;
			float diff_xy = diff.Xy.LengthFast;
			float phi = -(float)Math.Atan2 (diff.Z, diff_xy);
			float theta = (float)Math.Atan2 (diff.Y, diff.X);
			Matrix4 backgroundOrientMat = Matrix4.CreateRotationY (phi) * Matrix4.CreateRotationZ (theta);
			Matrix4 middlePlacementMat = backgroundOrientMat * Matrix4.CreateTranslation (middleView);
			Matrix4 startPlacementMat = Matrix4.CreateTranslation (startView);

			float laserLength = diff.LengthFast;
			float middleBackgroundWidth = laser.parameters.backgroundWidth;
			float middleBackgroundLength = laserLength + middleBackgroundWidth;
			float overlayBackgroundWidth = middleBackgroundWidth * laser.parameters.overlayWidthRatio;
			float overlayBackgroundLength = laserLength + overlayBackgroundWidth;
			float startBackgroundWidth = middleBackgroundWidth * laser.parameters.startPointScale;
			float startOverlayWidth = startBackgroundWidth * laser.parameters.overlayWidthRatio;

			if (middleBackgroundSprite != null) {
				GL.Material(MaterialFace.Front, MaterialParameter.Emission, laser.parameters.backgroundColor);
				GL.BindTexture (TextureTarget.Texture2D, middleBackgroundSprite.TextureID);
				GL.LoadMatrix (ref middlePlacementMat);

				_updateMiddleMesh (middleBackgroundLength, middleBackgroundWidth);
				_middleMesh.renderMesh (renderConfig);

				if (startBackgroundSprite != null) {
				//if (false) {
					GL.BindTexture (TextureTarget.Texture2D, startBackgroundSprite.TextureID);
					var mat = Matrix4.CreateScale (startBackgroundWidth, startBackgroundWidth, 1f)
					               * startPlacementMat;
					GL.LoadMatrix (ref mat);
					SSTexturedQuad.SingleFaceInstance.DrawArrays (renderConfig, PrimitiveType.Triangles);
				}
			}
			if (middleOverlaySprite != null) {
			//if (false) {
				GL.Material(MaterialFace.Front, MaterialParameter.Emission, laser.parameters.overlayColor);
				GL.BindTexture (TextureTarget.Texture2D, middleOverlaySprite.TextureID);
				GL.LoadMatrix (ref middlePlacementMat);

				_updateMiddleMesh (overlayBackgroundLength, overlayBackgroundWidth);
				_middleMesh.renderMesh (renderConfig);

				if (startOverlaySprite != null) {
					//if (false) {
					GL.BindTexture (TextureTarget.Texture2D, startOverlaySprite.TextureID);
					var mat = Matrix4.CreateScale (startOverlayWidth, startOverlayWidth, 1f)
						* startPlacementMat;
					GL.LoadMatrix (ref mat);
					SSTexturedQuad.SingleFaceInstance.DrawArrays (renderConfig, PrimitiveType.Triangles);
				}
			}
		}

		protected void _initMiddleMesh()
		{
			_middleVertices = new SSVertex_PosTex[8];
			_middleVertices [0].TexCoord = new Vector2 (0f, 0f);
			_middleVertices [1].TexCoord = new Vector2 (0f, 1f);

			_middleVertices [2].TexCoord = new Vector2 (0.5f, 0f);
			_middleVertices [3].TexCoord = new Vector2 (0.5f, 1f);

			_middleVertices [4].TexCoord = new Vector2 (0.5f, 0f);
			_middleVertices [5].TexCoord = new Vector2 (0.5f, 1f);

			_middleVertices [6].TexCoord = new Vector2 (1f, 0f);
			_middleVertices [7].TexCoord = new Vector2 (1f, 1f);

			_middleVertices [4].TexCoord = new Vector2 (0.5f, 0f);
			_middleVertices [5].TexCoord = new Vector2 (0.5f, 1f);

			_middleMesh = new SSIndexedMesh<SSVertex_PosTex>(_middleVertices, _middleIndices);
		}

		protected void _updateMiddleMesh(float laserLength, float meshWidth)
		{
			float halfWidth = meshWidth / 2f;
			float halfLength = laserLength / 2f;

			for (int i = 0; i < 8; i += 2) {
				_middleVertices [i].Position.Y = +halfWidth;
				_middleVertices [i + 1].Position.Y = -halfWidth;
			}

			_middleVertices [0].Position.X = _middleVertices[1].Position.X = -halfLength - halfWidth;
			_middleVertices [2].Position.X = _middleVertices[3].Position.X = -halfLength + halfWidth;
			_middleVertices [4].Position.X = _middleVertices[5].Position.X = +halfLength - halfWidth;
			_middleVertices [6].Position.X = _middleVertices[7].Position.X = +halfLength + halfWidth;

			_middleMesh.UpdateVertices (_middleVertices);
		}

		#if false
		/// <summary>
		/// Adds the laser to a list of lasers. Laser start, ends, fade and other effects
		/// are to be updated from somewhere else.
		/// </summary>
		/// <returns>The handle to LaserInfo which can be used for updating start and 
		/// end.</returns>
		public SSLaser addLaser(SSLaserParameters parameters)
		{
		var li = new SSLaser();
		li.start = Vector3.Zero;
		li.end = Vector3.Zero;
		li.parameters = parameters;
		_lasers.Add (li);
		return li;
		}

		public void removeLaser(SSLaser laser)
		{
		_lasers.Remove (laser);
		}
		#endif
	}
}

