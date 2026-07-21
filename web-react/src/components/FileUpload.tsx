import type { ReactNode } from 'react'
import { App, Upload, type UploadProps } from 'antd'
import { fileApi } from '@/api'
import { uploadChunked } from '@/utils/chunkUpload'
import { translateError } from '@/utils/error'
import type { FileUploadOutput } from '@/types/api'

/**
 * 上传编排(抽出便于单测):`chunked` 走分片/秒传/续传(uploadChunked),否则整文件直传(fileApi.upload)。
 * loadingChange 在真正开始/结束各发一次(try/finally 兜住);失败经 onFailMessage 弹错并 onError。
 */
export async function performUpload(
  file: File,
  chunked: boolean,
  cb: {
    onProgress?: (percent: number) => void
    onUploaded?: (out: FileUploadOutput) => void
    onLoadingChange?: (loading: boolean) => void
    onSuccess?: (out: FileUploadOutput) => void
    onError?: (e: unknown) => void
    onFailMessage?: (msg: string) => void
  },
): Promise<void> {
  cb.onLoadingChange?.(true)
  try {
    const out = chunked ? await uploadChunked(file, (p) => cb.onProgress?.(p)) : await fileApi.upload(file)
    cb.onUploaded?.(out)
    cb.onSuccess?.(out)
  } catch (e) {
    cb.onFailMessage?.(translateError(e))
    cb.onError?.(e)
  } finally {
    cb.onLoadingChange?.(false)
  }
}

export interface FileUploadProps extends Omit<UploadProps, 'customRequest'> {
  /** 走分片/断点续传/秒传编排;否则整文件直传。 */
  chunked?: boolean
  onUploaded?: (out: FileUploadOutput) => void
  /** 上传真正开始/结束各一次,给触发器加"上传中"反馈用。 */
  onLoadingChange?: (loading: boolean) => void
  children?: ReactNode
}

/**
 * 封 antd `Upload` 的 `customRequest`,走 api 层上传(自动带 Bearer),成功 `onUploaded(out)`。
 * 其余 Upload props(accept/multiple/showUploadList…)经 rest 透传。对应 Vue 侧 `FileUpload/index.vue`。
 */
export function FileUpload({ chunked, onUploaded, onLoadingChange, children, ...rest }: FileUploadProps) {
  const { message } = App.useApp()
  const customRequest: UploadProps['customRequest'] = ({ file, onProgress, onSuccess, onError }) => {
    void performUpload(file as File, !!chunked, {
      onProgress: (p) => onProgress?.({ percent: p }),
      onUploaded,
      onLoadingChange,
      onSuccess: (out) => onSuccess?.(out),
      onError: (e) => onError?.(e as Error),
      onFailMessage: (msg) => message.error(msg),
    })
  }
  return (
    <Upload {...rest} customRequest={customRequest}>
      {children}
    </Upload>
  )
}
