export const FieldError = ({ message }: { message?: string }) => {
    if (!message) return null
    return <p className="text-sm text-destructive">{message}</p>
}